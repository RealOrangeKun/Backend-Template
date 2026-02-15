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