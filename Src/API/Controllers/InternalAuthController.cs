using Microsoft.AspNetCore.Mvc;
using Application.DTOs.InternalAuth;
using Application.Services;
using Application.Utils;
using Application.DTOs.Misc;

namespace API.Controllers;
[ApiController]
[Route("api/internal-auth")]
public class InternalAuthController(AuthService authService) : BaseApiController
{
    private readonly AuthService _authService = authService;
    [HttpPost("register")]
    [ProducesResponseType(typeof(SuccessApiResponse<RegisterResponseDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequestDto registerRequest,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(registerRequest, cancellationToken);
        return SuccessResponse(result, StatusCodes.Status201Created, "Registration successful.");
    }

    [HttpPost("login")]
        [ProducesResponseType(typeof(SuccessApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto loginRequest, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(loginRequest, cancellationToken);
        return SuccessResponse(result, StatusCodes.Status200OK, "Login successful.");
    }

    // [HttpPost("confirm-email")]
    // public async Task<ActionResult<ConfirmEmailResponseDto>> ConfirmEmail(
    //     [FromBody] ConfirmEmailRequestDto confirmEmailRequest,
    //     CancellationToken cancellationToken)
    // {
        
    // }

    // [HttpPost("resend-confirmation-email")]
    // public async Task<ActionResult<ResendConfirmationEmailResponseDto>> ResendConfirmationEmail([FromBody] ResendConfirmationEmailRequestDto resendConfirmationEmailRequest, CancellationToken cancellationToken)
    // {
        
    // }

    // [HttpPost("forget-password")]
    // public async Task<ActionResult<ForgetPasswordResponseDto>> ForgetPassword([FromBody] ForgetPasswordRequestDto forgetPasswordRequest, CancellationToken cancellationToken)
    // {
        
    // }

    // [HttpPost("reset-password")]
    // public async Task<ActionResult<ResetPasswordResponseDto>> ResetPassword([FromBody] ResetPasswordRequestDto resetPasswordRequest, CancellationToken cancellationToken)
    // {
        
    // }
}
