using API.Extensions;
using Application.DTOs.ExternalAuth;
using Application.Services.Interfaces;
using Application.Utils;
using Asp.Versioning;
using Domain.Shared;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// External Authentication Controller
/// 
/// Manages OAuth2-based authentication flows with external providers.
/// Currently supports Google OAuth2 for seamless social login integration.
/// 
/// **Key Features:**
/// - Single Sign-On (SSO) via Google OAuth2
/// - Automatic user account creation on first login
/// - JWT token generation for authenticated sessions
/// - Secure refresh token management via HttpOnly cookies
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/external-auth")]
public class ExternalAuthController(IExternalAuthService authService, ILogger<ExternalAuthController> logger) : ControllerBase
{
    private readonly IExternalAuthService _authService = authService;
    private readonly ILogger<ExternalAuthController> _logger = logger;

    /// <summary>
    /// Authenticate with Google OAuth2
    /// </summary>
    /// <remarks>
    /// **Endpoint:** `POST /api/v1/external-auth/google-login`
    /// 
    /// **Description:**
    /// Authenticates users via Google OAuth2. Handles both existing user login and new user registration.
    /// On successful authentication, creates/updates user account and returns JWT tokens.
    /// 
    /// **Prerequisites:**
    /// 1. Google OAuth2 credentials configured in environment variables:
    ///    - `Google_ClientId`: OAuth app client ID
    ///    - `Google_ClientSecret`: OAuth app client secret
    /// 2. User must have completed Google OAuth flow and obtained ID token
    /// 3. Frontend should handle Google Sign-In and obtain ID token before calling this endpoint
    /// 
    /// **Request Body:**
    /// ```json
    /// {
    ///   "idToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6ImtleV9pZCJ9..."
    /// }
    /// ```
    /// 
    /// **Success Response (200 OK):**
    /// ```json
    /// {
    ///   "statusCode": 200,
    ///   "message": "Google login successful",
    ///   "data": {
    ///     "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    ///     "userId": "123e4567-e89b-12d3-a456-426614174000",
    ///     "email": "user@gmail.com",
    ///     "isNewUser": false
    ///   },
    ///   "errors": [],
    ///   "traceId": "0HN41ONCDJ3GH:00000001"
    /// }
    /// ```
    /// 
    /// **Cookies Set (Secure HttpOnly):**
    /// - `refreshToken`: Secure refresh token for token rotation (30 days expiration)
    /// 
    /// **Error Responses:**
    /// - `400 Bad Request`: Missing or invalid ID token
    /// - `401 Unauthorized`: Invalid or expired Google token
    /// - `401 Unauthorized`: Google token signature verification failed
    /// 
    /// **User Account Behavior:**
    /// 
    /// **First Login (New User):**
    /// 1. Validates Google ID token with Google's public certificates
    /// 2. Extracts user information (email, name from token payload)
    /// 3. Creates new user account with auth_scheme = 'Google'
    /// 4. Email is automatically marked as verified (Google verified it)
    /// 5. Generates JWT access token
    /// 6. Returns response with `isNewUser: true`
    /// 
    /// **Subsequent Logins (Existing User):**
    /// 1. Validates Google ID token
    /// 2. Finds existing user by email
    /// 3. Updates last login timestamp
    /// 4. Generates JWT access token
    /// 5. Returns response with `isNewUser: false`
    /// 
    /// **Account Linking (Future):**
    /// - Users registered with email/password cannot login via Google to same email
    /// - Google accounts are separate from internal accounts (no auto-linking)
    /// - Each auth provider maintains separate password/credentials
    /// 
    /// **Business Logic Flow:**
    /// 1. Receives Google ID token from frontend
    /// 2. Validates token signature using Google's public certificates
    /// 3. Extracts and verifies token claims (iss, aud, exp)
    /// 4. Checks if user exists by email
    /// 5. Creates or updates user account
    /// 6. Generates JWT access token (30 minutes expiration)
    /// 7. Generates refresh token (30 days expiration)
    /// 8. Sets refresh token in HttpOnly cookie
    /// 9. Returns access token in response body
    /// 
    /// **Security Measures:**
    /// - Token signature verified with Google's public key
    /// - Expiration claim checked (prevents replay attacks)
    /// - Issuer and audience validated
    /// - HttpOnly cookies prevent XSS token theft
    /// - Access tokens short-lived (30 minutes)
    /// - Refresh tokens rotation-capable
    /// 
    /// **OIDC Token Structure Example:**
    /// ```json
    /// {
    ///   "iss": "https://accounts.google.com",
    ///   "azp": "YOUR_CLIENT_ID.apps.googleusercontent.com",
    ///   "aud": "YOUR_CLIENT_ID.apps.googleusercontent.com",
    ///   "sub": "123456789",
    ///   "email": "user@gmail.com",
    ///   "email_verified": true,
    ///   "iat": 1704067200,
    ///   "exp": 1704070800,
    ///   "name": "John Doe",
    ///   "picture": "https://lh3.googleusercontent.com/...",
    ///   "given_name": "John",
    ///   "family_name": "Doe",
    ///   "locale": "en"
    /// }
    /// ```
    /// 
    /// **Frontend Integration Example (JavaScript):**
    /// ```javascript
    /// // After Google Sign-In button clicked
    /// const handleGoogleSignIn = async (credentialResponse) => {
    ///   const idToken = credentialResponse.credential; // JWT from Google
    ///   
    ///   const response = await fetch('/api/v1/external-auth/google-login', {
    ///     method: 'POST',
    ///     headers: { 'Content-Type': 'application/json' },
    ///     body: JSON.stringify({ idToken }),
    ///     credentials: 'include' // Include cookies (refreshToken)
    ///   });
    ///   
    ///   const data = await response.json();
    ///   if (response.ok) {
    ///     localStorage.setItem('accessToken', data.data.accessToken);
    ///     // Redirect to dashboard
    ///   }
    /// }
    /// ```
    /// 
    /// **Testing:**
    /// 1. Use Google OAuth playground: https://developers.google.com/oauthplayground/
    /// 2. Get ID token for your test account
    /// 3. Copy token and send to this endpoint
    /// 4. Verify response contains accessToken and userId
    /// 
    /// **Troubleshooting:**
    /// - "Invalid token signature": Verify Google credentials in .env
    /// - "Invalid audience": Check Google_ClientId matches frontend OAuth app
    /// - "Expired token": Ensure client system clock is synced
    /// - "Invalid issuer": Token must be from https://accounts.google.com
    /// </remarks>
    [HttpPost("google-login")]
    [ProducesResponseType(typeof(SuccessApiResponse<GoogleAuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleAuthRequestDto authRequest, CancellationToken ct)
    {
        _logger.LogInformation("Processing Google login request");
        var result = await _authService.GoogleLoginAsync(authRequest, ct);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Google login successful for user {UserId}", result.Data.Data.UserId);
            var refreshToken = result.Data.Data.RefreshToken;
            Response.Cookies.Append(
                "refreshToken",
                refreshToken,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                }
            );
        }

        return this.ToActionResult(result);
    }
}