using API.ActionFilters;
using API.Extensions;
using Application.DTOs.User;
using Application.Services.Interfaces;
using Application.Utils;
using Asp.Versioning;
using Domain.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// User Profile Controller
/// 
/// Manages authenticated user profile operations including viewing and updating profile information.
/// All endpoints require valid JWT access token in Authorization header.
/// 
/// **Authentication Required:** Bearer JWT Token (obtained from /internal-auth/login or /external-auth/google-login)
/// 
/// **Key Features:**
/// - View authenticated user's profile
/// - Update user profile information (email, phone, address)
/// - Idempotent profile updates with deduplication
/// - Real-time profile synchronization
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
[Authorize]
public class UsersController(IUserFacadeService userFacade) : ControllerBase
{
    private readonly IUserFacadeService _userFacade = userFacade;
    
    /// <summary>
    /// Update authenticated user's profile
    /// </summary>
    /// <remarks>
    /// **Endpoint:** `PATCH /api/v1/users/profile`
    /// 
    /// **Authentication:** Required - Bearer JWT Token
    /// ```
    /// Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
    /// ```
    /// 
    /// **Description:**
    /// Updates profile information for the authenticated user. Supports partial updates
    /// (only provided fields are updated). Idempotent - safe to retry on network failure.
    /// 
    /// **Required Headers:**
    /// - `Authorization: Bearer {accessToken}`: Valid JWT access token
    /// - `Idempotency-Key` (string, UUID): Unique key for deduplication on retry
    /// 
    /// **Request Body (all fields optional):**
    /// ```json
    /// {
    ///   "email": "newemail@example.com",
    ///   "phoneNumber": "+1-555-0123",
    ///   "address": "123 Main St, Springfield, IL 62701"
    /// }
    /// ```
    /// 
    /// **Success Response (200 OK):**
    /// ```json
    /// {
    ///   "statusCode": 200,
    ///   "message": "Profile updated successfully",
    ///   "data": {
    ///     "userId": "123e4567-e89b-12d3-a456-426614174000",
    ///     "email": "newemail@example.com",
    ///     "username": "john_doe",
    ///     "phoneNumber": "+1-555-0123",
    ///     "address": "123 Main St, Springfield, IL 62701",
    ///     "isEmailVerified": false
    ///   },
    ///   "errors": [],
    ///   "traceId": "0HN41ONCDJ3GH:00000001"
    /// }
    /// ```
    /// 
    /// **Error Responses:**
    /// - `400 Bad Request`: Validation failed (invalid email format, etc.)
    /// - `400 Bad Request`: Missing Idempotency-Key header
    /// - `401 Unauthorized`: Invalid or expired access token
    /// - `409 Conflict`: Email already in use by another user
    /// - `422 Unprocessable Entity`: Internal validation error
    /// 
    /// **Business Logic:**
    /// 1. Extracts user ID from JWT claims
    /// 2. Checks Idempotency-Key for duplicate requests
    /// 3. Returns cached response if duplicate request detected
    /// 4. Validates input fields (email format, phone format, etc.)
    /// 5. Checks email uniqueness if email being changed
    /// 6. Updates user record in database
    /// 7. If email changed, marks as unverified (requires re-confirmation)
    /// 8. Caches response with Idempotency-Key
    /// 9. Returns updated profile
    /// 
    /// **Idempotency Behavior:**
    /// - Same `Idempotency-Key` within 24 hours returns same response
    /// - Prevents duplicate updates on network retries
    /// - Ensures data consistency for critical profile changes
    /// 
    /// **Email Change Behavior:**
    /// - Email verified status reset to false
    /// - New confirmation email sent to new address
    /// - User must confirm new email within 6 hours
    /// - Old email still accessible until new email confirmed
    /// 
    /// **Validation Rules:**
    /// - Email: Valid RFC 5322 format (e.g., user@example.com)
    /// - Phone Number: Optional, any format (stored as-is)
    /// - Address: Optional, free-form text up to 500 characters
    /// 
    /// **Security Notes:**
    /// - JWT token must be valid and not expired
    /// - User can only update their own profile (cannot update others)
    /// - Email changes require re-verification
    /// - Password cannot be changed via this endpoint (use /internal-auth/reset-password)
    /// 
    /// **Example cURL Command:**
    /// ```bash
    /// curl -X PATCH http://localhost:5000/api/v1/users/profile \
    ///   -H "Authorization: Bearer YOUR_JWT_TOKEN" \
    ///   -H "Content-Type: application/json" \
    ///   -H "Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000" \
    ///   -d '{
    ///     "email": "newemail@example.com",
    ///     "phoneNumber": "+1-555-0123",
    ///     "address": "123 Main St"
    ///   }'
    /// ```
    /// </remarks>
    [HttpPatch("profile")]
    [Idempotent]
    [ProducesResponseType(typeof(SuccessApiResponse<UpdateUserRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserRequestDto request, CancellationToken ct)
    {
        var userIdResult = this.GetAuthenticatedUserId();
        if (!userIdResult.IsSuccess)
        {
            return this.ToActionResult(Result<SuccessApiResponse>.Failure(userIdResult.Error));
        }

        var result = await _userFacade.UpdateProfileAsync(userIdResult.Data, request, ct);
        return this.ToActionResult(result);
    }

    /// <summary>
    /// Get authenticated user's profile
    /// </summary>
    /// <remarks>
    /// **Endpoint:** `GET /api/v1/users/profile`
    /// 
    /// **Authentication:** Required - Bearer JWT Token
    /// ```
    /// Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
    /// ```
    /// 
    /// **Description:**
    /// Retrieves complete profile information for the authenticated user.
    /// No query parameters required - returns profile of token bearer.
    /// 
    /// **Request Headers:**
    /// - `Authorization: Bearer {accessToken}`: Valid JWT access token
    /// 
    /// **Request Parameters:** None
    /// 
    /// **Success Response (200 OK):**
    /// ```json
    /// {
    ///   "statusCode": 200,
    ///   "message": "Profile retrieved successfully",
    ///   "data": {
    ///     "userId": "123e4567-e89b-12d3-a456-426614174000",
    ///     "email": "user@example.com",
    ///     "username": "john_doe",
    ///     "phoneNumber": "+1-555-0123",
    ///     "address": "123 Main St, Springfield, IL 62701",
    ///     "isEmailVerified": true,
    ///     "role": "User",
    ///     "authScheme": "Internal",
    ///     "createdAt": "2026-02-15T10:00:00Z",
    ///     "lastModifiedAt": "2026-02-15T14:30:00Z"
    ///   },
    ///   "errors": [],
    ///   "traceId": "0HN41ONCDJ3GH:00000002"
    /// }
    /// ```
    /// 
    /// **Error Responses:**
    /// - `401 Unauthorized`: Invalid or expired access token
    /// - `404 Not Found`: User not found (should not happen for valid tokens)
    /// 
    /// **Response Fields:**
    /// | Field | Type | Description |
    /// |-------|------|-------------|
    /// | userId | UUID | Unique user identifier |
    /// | email | string | User's email address |
    /// | username | string | Unique username for internal auth |
    /// | phoneNumber | string | Optional contact phone number |
    /// | address | string | Optional mailing address |
    /// | isEmailVerified | boolean | Email verification status |
    /// | role | string | User role (User, Admin, etc.) |
    /// | authScheme | string | Authentication method (Internal, Google, etc.) |
    /// | createdAt | ISO 8601 | Account creation timestamp |
    /// | lastModifiedAt | ISO 8601 | Last profile update timestamp |
    /// 
    /// **Business Logic:**
    /// 1. Extracts user ID from JWT claims
    /// 2. Validates user exists in database
    /// 3. Retrieves complete user profile
    /// 4. Formats response with all user information
    /// 5. Returns 200 OK with profile data
    /// 
    /// **Security Notes:**
    /// - JWT token must be valid and not expired
    /// - User can only retrieve their own profile
    /// - Password hash never included in response
    /// - Refresh token never included in response
    /// 
    /// **Caching Recommendations (Client-Side):**
    /// - Cache profile for 5 minutes to reduce API calls
    /// - Invalidate cache after successful profile update
    /// - Refresh on login/token refresh
    /// 
    /// **Example cURL Command:**
    /// ```bash
    /// curl -X GET http://localhost:5000/api/v1/users/profile \
    ///   -H "Authorization: Bearer YOUR_JWT_TOKEN"
    /// ```
    /// 
    /// **Example JavaScript Fetch:**
    /// ```javascript
    /// const getProfile = async (token) => {
    ///   const response = await fetch('http://localhost:5000/api/v1/users/profile', {
    ///     method: 'GET',
    ///     headers: {
    ///       'Authorization': `Bearer ${token}`,
    ///       'Content-Type': 'application/json'
    ///     }
    ///   });
    ///   
    ///   if (!response.ok) {
    ///     throw new Error(`Profile fetch failed: ${response.status}`);
    ///   }
    ///   
    ///   return response.json();
    /// }
    /// ```
    /// </remarks>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(SuccessApiResponse<GetUserProfileResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var userIdResult = this.GetAuthenticatedUserId();
        if (!userIdResult.IsSuccess)
        {
            return this.ToActionResult(Result<SuccessApiResponse>.Failure(userIdResult.Error));
        }
        
        var result = await _userFacade.GetProfileAsync(userIdResult.Data, ct);
        return this.ToActionResult(result);
    }
}
