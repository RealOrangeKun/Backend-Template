using Application.DTOs.Auth;
using Application.Services.Interfaces;
using Domain.Shared;
using Application.Constants.ApiErrors;
using Application.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;
using Application.Utils;
using Domain.Models.User;
using Application.Services.Implementations.Auth.InternalAuth;
using Application.Services.Implementations.Misc;
using Application.Services.Interfaces.Auth.InternalAuth;
using Domain.Extensions;
using Application.DTOs.Auth.InternalAuth;

namespace Application.Services.Implementations;

public class InternalPasswordResetService(
    IUserRepository userRepository,
    ILogger<InternalPasswordResetService> logger,
    PasswordResetEmailSender emailService,
    IOtpService<PasswordResetOtpPayload> otpService
) : IInternalPasswordResetService
{
    private readonly IUserRepository _userRepository = userRepository;
    private readonly PasswordResetEmailSender _emailService = emailService;
    private readonly ILogger<InternalPasswordResetService> _logger = logger;
    private readonly IOtpService<PasswordResetOtpPayload> _otpService = otpService;

    public async Task<Result<SuccessApiResponse>> ForgetPasswordAsync(ForgetPasswordRequestDto forgetPasswordRequest, CancellationToken cancellationToken)
    {
        var email = forgetPasswordRequest.Email;
        var user = await _userRepository.GetUserByEmailAsync(email, cancellationToken);
        var validationResult = await ValidateForgetPasswordRequestAsync(user, cancellationToken);
        if (validationResult.IsSuccess == false)
        {
            return validationResult;
        }

        var otp = OtpGenerator.GenerateOtp();
        await _otpService.CacheAsync(new PasswordResetOtpPayload(user!.Id), otp, cancellationToken);
        await _emailService.SendAsync(email, otp, cancellationToken);
        _logger.LogInformation("Password reset email sent successfully to {Email}", email);

        return Result<SuccessApiResponse>.Success(new SuccessApiResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Password reset email sent successfully. Please check your email for the reset code.",
        });
    }
    private async Task<Result<SuccessApiResponse>> ValidateForgetPasswordRequestAsync(User? user, CancellationToken cancellationToken)
    {
        if (user == null)
        {
            _logger.LogWarning("Forget password failed: User not found");
            return Result<SuccessApiResponse>.Failure(UserErrors.UserNotFound);
        }
        if (user.IsNotEmailVerified())
        {
            _logger.LogWarning("Forget password failed: Email not confirmed");
            return Result<SuccessApiResponse>.Failure(AuthErrors.EmailNotConfirmed);
        }
        return Result<SuccessApiResponse>.Success(default!);
    }

    public async Task<Result<SuccessApiResponse>> ResetPasswordAsync(ResetPasswordRequestDto resetPasswordRequest, CancellationToken cancellationToken)
    {
        var userId = (await _otpService.GetDataAsync(resetPasswordRequest.Otp, cancellationToken))?.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
        {
            _logger.LogWarning("Password reset failed: Invalid OTP");
            return Result<SuccessApiResponse>.Failure(AuthErrors.InvalidToken);
        }
        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        var validationResult = await ValidateResetPasswordRequestAsync(user, cancellationToken);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        var newHashedPassword = BCrypt.Net.BCrypt.HashPassword(resetPasswordRequest.NewPassword, BCrypt.Net.BCrypt.GenerateSalt());
        await _userRepository.UpdatePasswordByEmailAsync(user!.Email!, newHashedPassword, cancellationToken);
        _logger.LogInformation("Password reset successfully for user {UserId}", userId);

        return Result<SuccessApiResponse>.Success(new SuccessApiResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Password reset successful.",
        });
    }
    private async Task<Result<SuccessApiResponse>> ValidateResetPasswordRequestAsync(User? user, CancellationToken cancellationToken)
    {
        if (user == null || user.IsNotEmailVerified())
        {
            _logger.LogWarning("Password reset failed: Invalid token for user {UserId}", user?.Id);
            return Result<SuccessApiResponse>.Failure(AuthErrors.InvalidToken);
        }
        return Result<SuccessApiResponse>.Success(default!);
    }
}