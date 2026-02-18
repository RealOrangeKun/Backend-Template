using Application.Constants.ApiErrors;
using Application.DTOs.Auth;
using Application.Repositories.Interfaces;
using Application.Services.Interfaces;
using Application.Utils;
using Domain.Models.User;
using Domain.Models.UserDevice;
using Domain.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Application.Services.Implementations;

public class UserConfirmationService(
    IUserRepository userRepository,
    IUserDevicesRepository userDevicesRepository,
    IEmailService emailService,
    ILogger<UserConfirmationService> logger,
    ConfirmationTokenCacheService tokenCacheService) 
    : IUserConfirmationService
{
    private readonly IUserRepository _userRepository = userRepository;
    private readonly IUserDevicesRepository _userDevicesRepository = userDevicesRepository;
    private readonly IEmailService _emailService = emailService;
    private readonly ILogger<UserConfirmationService> _logger = logger;
    private readonly ConfirmationTokenCacheService _tokenCacheService = tokenCacheService;

    public async Task<Result<SuccessApiResponse<ConfirmEmailResponseDto>>> ConfirmEmailAsync(ConfirmEmailRequestDto confirmEmailRequest, Guid deviceId, CancellationToken cancellationToken)
    {
        var userId = await _tokenCacheService.GetUserIdByTokenAsync($"new_user:{confirmEmailRequest.Token}", cancellationToken);
        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        var validationResult = await ValidateConfirmEmailRequestAsync(user, cancellationToken);
        if (!validationResult.IsSuccess)
        {
            return Result<SuccessApiResponse<ConfirmEmailResponseDto>>.Failure(validationResult.Error);
        }

        await _userRepository.ConfirmEmailAsync(user!.Email!, cancellationToken);
        await AddUserDevice(user.Id, deviceId, cancellationToken);

        await _tokenCacheService.DeleteTokenAsync($"new_user:{confirmEmailRequest.Token}", cancellationToken);

        return Result<SuccessApiResponse<ConfirmEmailResponseDto>>.Success(new SuccessApiResponse<ConfirmEmailResponseDto>
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Email confirmation successful.",
            Data = new ConfirmEmailResponseDto { DeviceId = deviceId }
        });
    }
    private async Task<Result<SuccessApiResponse>> ValidateConfirmEmailRequestAsync(User? user, CancellationToken cancellationToken)
    {
        if (UserNotFound(user))
        {
            _logger.LogWarning("Email confirmation failed: User not found or token expired");
            return Result<SuccessApiResponse>.Failure(AuthErrors.InvalidToken);
        }

        if (user!.IsEmailVerified)
        {
            _logger.LogWarning("Email confirmation failed: Email {Email} is already confirmed", user.Email);
            return Result<SuccessApiResponse>.Failure(AuthErrors.EmailAlreadyConfirmed);
        }

        return Result<SuccessApiResponse>.Success(default!);
    }
    private async Task AddUserDevice(Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var userDevice = new UserDevice(userId, deviceId);
        await _userDevicesRepository.AddUserDeviceAsync(userDevice, cancellationToken);
        _logger.LogInformation("Device {DeviceId} added for user {UserId}", deviceId, userId);
    }

    public async Task<Result<SuccessApiResponse>> ResendConfirmationEmailAsync(ResendConfirmationEmailRequestDto resendConfirmationEmailRequest, CancellationToken cancellationToken)
    {
        var email = resendConfirmationEmailRequest.Email;
        var user = await _userRepository.GetUserByEmailAsync(email, cancellationToken);
        var validationResult = await ValidateResendConfirmationEmailRequestAsync(user, cancellationToken);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }
        _logger.LogInformation("Generating new confirmation token and resending email to {Email}", email);

        var confirmationToken = ConfirmationTokenCacheService.GenerateRandomToken();
        var storedToken = $"new_user:{confirmationToken}";
        await _tokenCacheService.SetTokenAsync(storedToken, user!.Id, cancellationToken);

        await _emailService.SendConfirmationEmailAsync(email, confirmationToken, cancellationToken);
        _logger.LogInformation("Confirmation email resent successfully to {Email}", email);

        return Result<SuccessApiResponse>.Success(new SuccessApiResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Confirmation email resent successfully. Please check your email for the new confirmation code.",
        });
    }
    private async Task<Result<SuccessApiResponse>> ValidateResendConfirmationEmailRequestAsync(User? user, CancellationToken cancellationToken)
    {
        if (UserNotFound(user))
        {
            _logger.LogWarning("Resend confirmation failed: User not found");
            return Result<SuccessApiResponse>.Failure(UserErrors.UserNotFound);
        }
        if (user!.IsEmailVerified)
        {
            _logger.LogWarning("Resend confirmation failed: Email {Email} is already confirmed", user.Email);
            return Result<SuccessApiResponse>.Failure(AuthErrors.EmailAlreadyConfirmed);
        }
        return Result<SuccessApiResponse>.Success(default!);
    }
    private static bool UserNotFound(User? user)
    {
        if (user == null)
        {
            return true;
        }
        return false;
    }
}