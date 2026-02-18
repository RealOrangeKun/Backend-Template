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

namespace Application.Services.Implementations;

public class PasswordResetService(
    IUserRepository userRepository,
    IEmailService emailService,
    ILogger<PasswordResetService> logger,
    ConfirmationTokenCacheService tokenCacheService
) : IPasswordResetService
{
    private readonly IUserRepository _userRepository = userRepository;
    private readonly IEmailService _emailService = emailService;
    private readonly ILogger<PasswordResetService> _logger = logger;
    private readonly ConfirmationTokenCacheService _tokenCacheService = tokenCacheService;

    public async Task<Result<SuccessApiResponse>> ForgetPasswordAsync(ForgetPasswordRequestDto forgetPasswordRequest, CancellationToken cancellationToken)
    {
        var email = forgetPasswordRequest.Email;
        var user = await _userRepository.GetUserByEmailAsync(email, cancellationToken);
        var validationResult = await ValidateForgetPasswordRequestAsync(user, cancellationToken);
        if (validationResult.IsSuccess == false)
        {
            return validationResult;
        }
        _logger.LogInformation("Forget password request validated for {Email}", email);

        var confirmationToken = ConfirmationTokenCacheService.GenerateRandomToken();
        
        var storedToken = $"reset_password:{confirmationToken}";
        await _tokenCacheService.SetTokenAsync(storedToken, user!.Id, cancellationToken);

        await _emailService.SendPasswordResetEmailAsync(email, confirmationToken, cancellationToken);
        _logger.LogInformation("Password reset email sent successfully to {Email}", email);

        return Result<SuccessApiResponse>.Success(new SuccessApiResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Password reset email sent successfully. Please check your email for the reset code.",
        });
    }
    private async Task<Result<SuccessApiResponse>> ValidateForgetPasswordRequestAsync(User? user, CancellationToken cancellationToken)
    {
        if (UserNotFound(user))
        {
            _logger.LogWarning("Forget password failed: User not found");
            return Result<SuccessApiResponse>.Failure(UserErrors.UserNotFound);
        }
        if (EmailNotConfirmed(user!))
        {
            _logger.LogWarning("Forget password failed: Email not confirmed");
            return Result<SuccessApiResponse>.Failure(AuthErrors.EmailNotConfirmed);
        }
        return Result<SuccessApiResponse>.Success(default!);
    }

    public async Task<Result<SuccessApiResponse>> ResetPasswordAsync(ResetPasswordRequestDto resetPasswordRequest, CancellationToken cancellationToken)
    {
        var userId = await _tokenCacheService.GetUserIdByTokenAsync($"reset_password:{resetPasswordRequest.Token}", cancellationToken);
        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        var validationResult = await ValidateResetPasswordRequestAsync(user, cancellationToken);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        // User email is guaranteed to be non-null after validation
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
        if (InvalidUser(user))
        {
            _logger.LogWarning("Password reset failed: Invalid token for user {UserId}", user?.Id);
            return Result<SuccessApiResponse>.Failure(AuthErrors.InvalidToken);
        }
        return Result<SuccessApiResponse>.Success(default!);
    }
    private static bool InvalidUser(User? user)
    {
        if (UserNotFound(user) || EmailNotConfirmed(user!))
        {
            return true;
        }
        return false;
    }
    private static bool UserNotFound(User? user)
    {
        if (user == null)
        {
            return true;
        }
        return false;
    }
    private static bool EmailNotConfirmed(User user)
    {
        if (user.IsEmailVerified == false)
        {
            return true;
        }
        return false;
    }
}
