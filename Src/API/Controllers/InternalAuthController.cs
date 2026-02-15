using Microsoft.AspNetCore.Mvc;
using Application.DTOs.Auth;
using Application.Services.Interfaces;
using Application.DTOs.User;
using Application.Utils;
using Asp.Versioning;
using API.Extensions;
using API.ActionFilters;

namespace API.Controllers;

/// <summary>
/// Internal Authentication Controller
/// 
/// Manages user registration, login, email confirmation, password management, and token refresh.
/// This controller handles all authentication flows for users registering with email and password.
/// 
/// **Key Features:**
/// - User registration with email verification
/// - Secure login with JWT tokens and refresh cookies
/// - Email confirmation workflows
/// - Password reset functionality
/// - Token refresh mechanism
/// - Idempotency support for critical operations
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/internal-auth")]
public class InternalAuthController(IInternalAuthFacadeService authFacade) : ControllerBase
{
    private readonly IInternalAuthFacadeService _authFacade = authFacade;

    /// <summary>
    /// Register a new user account
    /// </summary>
    /// <remarks>
    /// **Endpoint:** `POST /api/v1/internal-auth/register`
    /// 
    /// **Description:**
    /// Creates a new user account with email and password. A confirmation email is automatically sent to the user.
    /// 
    /// **Required Headers:**
    /// - `Idempotency-Key` (string, UUID): Unique key to ensure idempotent registration. Prevents duplicate accounts on retry.
    /// 
    /// **Request Body:**
    /// ```json
    /// {
    ///   "email": "user@example.com",
    ///   "username": "john_doe",
    ///   "password": "SecurePassword123!",
    ///   "confirmPassword": "SecurePassword123!"
    /// }
    /// ```
    /// 
    /// **Success Response (201 Created):**
    /// ```json
    /// {
    ///   "statusCode": 201,
    ///   "message": "User registered successfully",
    ///   "data": {
    ///     "userId": "123e4567-e89b-12d3-a456-426614174000",
    ///     "email": "user@example.com",
    ///     "username": "john_doe"
    ///   },
    ///   "errors": [],
    ///   "traceId": "0HN41ONCDJ3GH:00000001"
    /// }
    /// ```
    /// 
    /// **Error Responses:**
    /// - `400 Bad Request`: Validation failed (weak password, invalid email format, etc.)
    /// - `409 Conflict`: Email or username already exists
    /// - `400 Bad Request`: Missing Idempotency-Key header
    /// 
    /// **Business Logic:**
    /// 1. Validates input (email format, password strength, matching confirmation)
    /// 2. Checks email and username uniqueness
    /// 3. Hashes password using BCrypt with work factor 11
    /// 4. Creates user in database
    /// 5. Sends confirmation email with verification link
    /// 6. Returns user details with 201 Created status
    /// 
    /// **Security Notes:**
    /// - Idempotency-Key prevents duplicate registration on network retries
    /// - Passwords are never returned in responses
    /// - Confirmation email required before full account activation
    /// </remarks>
    [HttpPost("register")]
    [Idempotent]
    [ProducesResponseType(typeof(SuccessApiResponse<RegisterResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequestDto registerRequest,
        CancellationToken cancellationToken)
    {
        var result = await _authFacade.RegisterAsync(registerRequest, cancellationToken);
        return this.ToActionResult(result);
    }

    /// <summary>
    /// Login with email and password
    /// </summary>
    /// <remarks>
    /// **Endpoint:** `POST /api/v1/internal-auth/login`
    /// 
    /// **Description:**
    /// Authenticates a user with email and password. Returns JWT access token and sets refresh token in secure HttpOnly cookie.
    /// 
    /// **Request Body:**
    /// ```json
    /// {
    ///   "email": "user@example.com",
    ///   "password": "SecurePassword123!"
    /// }
    /// ```
    /// 
    /// **Success Response (200 OK):**
    /// ```json
    /// {
    ///   "statusCode": 200,
    ///   "message": "Login successful",
    ///   "data": {
    ///     "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    ///     "userId": "123e4567-e89b-12d3-a456-426614174000",
    ///     "email": "user@example.com",
    ///     "username": "john_doe"
    ///   },
    ///   "errors": [],
    ///   "traceId": "0HN41ONCDJ3GH:00000002"
    /// }
    /// ```
    /// 
    /// **Cookies Set (Secure HttpOnly):**
    /// - `refreshToken`: Secure refresh token for token rotation (24 hours expiration)
    /// 
    /// **Error Responses:**
    /// - `400 Bad Request`: Invalid email or password format
    /// - `401 Unauthorized`: Invalid credentials
    /// - `403 Forbidden`: Email not verified
    /// - `404 Not Found`: User not found
    /// 
    /// **Business Logic:**
    /// 1. Validates email and password format
    /// 2. Finds user by email
    /// 3. Verifies password using BCrypt
    /// 4. Checks email verification status
    /// 5. Generates JWT access token (30 minutes expiration)
    /// 6. Generates refresh token (24 hours expiration)
    /// 7. Sets refresh token in HttpOnly cookie
    /// 8. Returns access token in response body
    /// 
    /// **Security Notes:**
    /// - Access tokens expire in 30 minutes
    /// - Refresh tokens are HttpOnly (immune to XSS)
    /// - Passwords are hashed and never stored/returned
    /// - Requires email verification to login
    /// </remarks>
    [HttpPost("login")]
    [ProducesResponseType(typeof(SuccessApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequestDto loginRequest, 
        CancellationToken cancellationToken)
    {
        var result = await _authFacade.LoginAsync(loginRequest, cancellationToken);
        if (result.IsSuccess)
        {
            var refreshToken = result.Data.Data.RefreshToken;
            this.AddRefreshTokenCookie(refreshToken);
        }
        return this.ToActionResult(result);
    }

    /// <summary>
    /// Confirm user email address
    /// </summary>
    /// <remarks>
    /// **Endpoint:** `POST /api/v1/internal-auth/confirm-email`
    /// 
    /// **Description:**
    /// Verifies user's email address using a confirmation token sent to their email. Required for account activation.
    /// 
    /// **Request Body:**
    /// ```json
    /// {
    ///   "email": "user@example.com",
    ///   "confirmationToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
    /// }
    /// ```
    /// 
    /// **Success Response (200 OK):**
    /// ```json
    /// {
    ///   "statusCode": 200,
    ///   "message": "Email confirmed successfully",
    ///   "data": {},
    ///   "errors": [],
    ///   "traceId": "0HN41ONCDJ3GH:00000003"
    /// }
    /// ```
    /// 
    /// **Error Responses:**
    /// - `400 Bad Request`: Invalid or expired token
    /// - `404 Not Found`: User not found
    /// - `400 Bad Request`: Email already confirmed
    /// 
    /// **Business Logic:**
    /// 1. Validates confirmation token
    /// 2. Checks token expiration (6 hours)
    /// 3. Finds user by email
    /// 4. Marks email as verified
    /// 5. Returns success response
    /// 
    /// **Security Notes:**
    /// - Tokens expire after 6 hours
    /// - One-time use tokens (cannot reuse)
    /// - Required before login is allowed
    /// </remarks>
    [HttpPost("confirm-email")]
    [ProducesResponseType(typeof(SuccessApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmEmail(
        [FromBody] ConfirmEmailRequestDto confirmEmailRequest,
        CancellationToken cancellationToken)
    {
       var result = await _authFacade.ConfirmEmailAsync(confirmEmailRequest, cancellationToken);
       return this.ToActionResult(result);
    }

    /// <summary>
    /// Resend confirmation email
    /// </summary>
    /// <remarks>
    /// **Endpoint:** `POST /api/v1/internal-auth/resend-confirmation-email`
    /// 
    /// **Description:**
    /// Resends confirmation email to unverified user accounts. Generates a new confirmation token.
    /// Useful when user didn't receive the initial email or token expired.
    /// 
    /// **Request Body:**
    /// ```json
    /// {
    ///   "email": "user@example.com"
    /// }
    /// ```
    /// 
    /// **Success Response (200 OK):**
    /// ```json
    /// {
    ///   "statusCode": 200,
    ///   "message": "Confirmation email resent successfully",
    ///   "data": {},
    ///   "errors": [],
    ///   "traceId": "0HN41ONCDJ3GH:00000004"
    /// }
    /// ```
    /// 
    /// **Error Responses:**
    /// - `404 Not Found`: User not found
    /// - `400 Bad Request`: Email already verified
    /// - `400 Bad Request`: Invalid email format
    /// 
    /// **Business Logic:**
    /// 1. Validates email format
    /// 2. Finds user by email
    /// 3. Checks if email not already confirmed
    /// 4. Generates new confirmation token
    /// 5. Sends confirmation email
    /// 6. Returns success response
    /// 
    /// **Note:** Can be called multiple times; each call generates a new token.
    /// </remarks>
    [HttpPost("resend-confirmation-email")]
    [ProducesResponseType(typeof(SuccessApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendConfirmationEmail(
        [FromBody] ResendConfirmationEmailRequestDto resendConfirmationEmailRequest, 
        CancellationToken cancellationToken)
    {
        var result = await _authFacade.ResendConfirmationEmailAsync(resendConfirmationEmailRequest, cancellationToken);
        return this.ToActionResult(result);
    }

    /// <summary>
    /// Request password reset
    /// </summary>
    /// <remarks>
    /// **Endpoint:** `POST /api/v1/internal-auth/forget-password`
    /// 
    /// **Description:**
    /// Initiates password reset process. Sends reset email with temporary token to verified user.
    /// User uses this token in the `/reset-password` endpoint to set new password.
    /// 
    /// **Request Body:**
    /// ```json
    /// {
    ///   "email": "user@example.com"
    /// }
    /// ```
    /// 
    /// **Success Response (200 OK):**
    /// ```json
    /// {
    ///   "statusCode": 200,
    ///   "message": "Password reset email sent successfully",
    ///   "data": {},
    ///   "errors": [],
    ///   "traceId": "0HN41ONCDJ3GH:00000005"
    /// }
    /// ```
    /// 
    /// **Error Responses:**
    /// - `404 Not Found`: User not found
    /// - `403 Forbidden`: Email not verified
    /// - `400 Bad Request`: Invalid email format
    /// 
    /// **Business Logic:**
    /// 1. Validates email format
    /// 2. Finds user by email
    /// 3. Checks email verification status
    /// 4. Generates reset token (valid 1 hour)
    /// 5. Sends reset email with token
    /// 6. Returns success response
    /// 
    /// **Security Notes:**
    /// - Only verified accounts can request password reset
    /// - Reset tokens expire after 1 hour
    /// - Tokens are one-time use
    /// </remarks>
    [HttpPost("forget-password")]
    [ProducesResponseType(typeof(SuccessApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ForgetPassword([FromBody] ForgetPasswordRequestDto forgetPasswordRequest, CancellationToken cancellationToken)
    {
        var result = await _authFacade.ForgetPasswordAsync(forgetPasswordRequest, cancellationToken);
        return this.ToActionResult(result);
    }

    /// <summary>
    /// Reset password with token
    /// </summary>
    /// <remarks>
    /// **Endpoint:** `POST /api/v1/internal-auth/reset-password`
    /// 
    /// **Description:**
    /// Completes password reset process using token from `/forget-password` email.
    /// Sets new password and invalidates old refresh tokens for security.
    /// 
    /// **Request Body:**
    /// ```json
    /// {
    ///   "email": "user@example.com",
    ///   "newPassword": "NewSecurePassword123!",
    ///   "confirmPassword": "NewSecurePassword123!",
    ///   "resetToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
    /// }
    /// ```
    /// 
    /// **Success Response (200 OK):**
    /// ```json
    /// {
    ///   "statusCode": 200,
    ///   "message": "Password reset successfully",
    ///   "data": {},
    ///   "errors": [],
    ///   "traceId": "0HN41ONCDJ3GH:00000006"
    /// }
    /// ```
    /// 
    /// **Error Responses:**
    /// - `400 Bad Request`: Invalid or expired token
    /// - `400 Bad Request`: Passwords don't match
    /// - `400 Bad Request`: Weak password
    /// - `404 Not Found`: User not found
    /// 
    /// **Business Logic:**
    /// 1. Validates password strength and match
    /// 2. Verifies reset token validity
    /// 3. Finds user by email
    /// 4. Hashes new password with BCrypt
    /// 5. Updates password in database
    /// 6. Invalidates all refresh tokens
    /// 7. Returns success response
    /// 
    /// **Security Notes:**
    /// - Reset tokens are one-time use
    /// - Old sessions are invalidated after password change
    /// - Forces re-login with new password
    /// </remarks>
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(SuccessApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto resetPasswordRequest, CancellationToken cancellationToken)
    {
        var result = await _authFacade.ResetPasswordAsync(resetPasswordRequest, cancellationToken);
        return this.ToActionResult(result);
    }

    /// <summary>
    /// Refresh access token
    /// </summary>
    /// <remarks>
    /// **Endpoint:** `POST /api/v1/internal-auth/refresh-token`
    /// 
    /// **Description:**
    /// Generates new access token using refresh token. Implements token rotation for enhanced security.
    /// Refresh token must be in HttpOnly cookie (set by /login or /external-auth/google-login).
    /// 
    /// **Request Body:**
    /// ```json
    /// {
    ///   "userId": "123e4567-e89b-12d3-a456-426614174000"
    /// }
    /// ```
    /// 
    /// **Cookies Required:**
    /// - `refreshToken` (HttpOnly): Secure refresh token from login response
    /// 
    /// **Success Response (200 OK):**
    /// ```json
    /// {
    ///   "statusCode": 200,
    ///   "message": "Token refreshed successfully",
    ///   "data": {
    ///     "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    ///     "userId": "123e4567-e89b-12d3-a456-426614174000"
    ///   },
    ///   "errors": [],
    ///   "traceId": "0HN41ONCDJ3GH:00000007"
    /// }
    /// ```
    /// 
    /// **Error Responses:**
    /// - `400 Bad Request`: Missing or invalid refresh token
    /// - `401 Unauthorized`: Refresh token expired or invalid
    /// - `400 Bad Request`: Invalid user ID
    /// 
    /// **Business Logic:**
    /// 1. Validates refresh token from cookie
    /// 2. Verifies token hasn't expired (24 hours)
    /// 3. Validates user ID
    /// 4. Finds user in database
    /// 5. Generates new access token (30 minutes)
    /// 6. Rotates refresh token (creates new one)
    /// 7. Sets new refresh token in cookie
    /// 8. Returns new access token
    /// 
    /// **Security Notes:**
    /// - Access tokens expire in 30 minutes (short-lived)
    /// - Refresh tokens expire in 24 hours
    /// - Token rotation prevents token fixation attacks
    /// - HttpOnly cookies prevent XSS token theft
    /// </remarks>
    [HttpPost("refresh-token")]
    [ProducesResponseType(typeof(SuccessApiResponse<RefreshTokenResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequestDto refreshTokenRequest, CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies["refreshToken"] ?? string.Empty;
        var result = await _authFacade.RefreshTokenAsync(refreshTokenRequest, refreshToken, cancellationToken);
        return this.ToActionResult(result);
    }
}