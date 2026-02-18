using Application.Constants.ApiErrors;
using Application.DTOs.Auth;
using Application.DTOs.Auth.InternalAuth;
using Application.Repositories.Interfaces;
using Application.Services.Implementations.Misc;
using Application.Services.Interfaces;
using Application.Services.Interfaces.Auth.InternalAuth;
using Application.Utils;
using Domain.Models.User;
using Domain.Models.UserDevice;
using Domain.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Application.Services.Implementations;

public class InternalUserVerificationService(
    IUserRepository userRepository,
    IOtpService<RegistrationOtpPayload> otpService,
    RegisterationConfirmationEmailSender emailService,
    ILogger<InternalUserVerificationService> logger,
    IUserDevicesRepository userDeviceRepository
) 
    : IInternalUserVerificationService
{
    private readonly IUserRepository _userRepository = userRepository;
    private readonly IOtpService<RegistrationOtpPayload> _otpService = otpService;
    private readonly RegisterationConfirmationEmailSender _emailService = emailService;
    private readonly IUserDevicesRepository _userDeviceRepository = userDeviceRepository;
    private readonly ILogger<InternalUserVerificationService> _logger = logger;

    public async Task<Result<SuccessApiResponse<ConfirmEmailResponseDto>>> ConfirmEmailAsync(ConfirmEmailRequestDto confirmEmailRequest, Guid deviceId, CancellationToken cancellationToken)
    {
        var userId = (await _otpService.GetDataAsync(confirmEmailRequest.Otp, cancellationToken))?.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
        {
            _logger.LogWarning("Email confirmation failed: Invalid OTP");
            return Result<SuccessApiResponse<ConfirmEmailResponseDto>>.Failure(AuthErrors.InvalidToken);
        }
        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        var validationResult = await ValidateConfirmEmailRequestAsync(user);
        if (!validationResult.IsSuccess)
        {
            return Result<SuccessApiResponse<ConfirmEmailResponseDto>>.Failure(validationResult.Error);
        }

        await _userRepository.ConfirmEmailAsync(user!.Email!, cancellationToken);
        await TrustDeviceAsync(userId, deviceId, cancellationToken);
        _logger.LogInformation("Email {Email} confirmed successfully for user ID {UserId}", user.Email, user.Id);

        return Result<SuccessApiResponse<ConfirmEmailResponseDto>>.Success(new SuccessApiResponse<ConfirmEmailResponseDto>
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Email confirmation successful.",
            Data = new ConfirmEmailResponseDto { DeviceId = deviceId }
        });
    }
    private async Task<Result<SuccessApiResponse>> ValidateConfirmEmailRequestAsync(User? user)
    {
        if (user == null)
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
    private async Task TrustDeviceAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var UserDevice = new UserDevice(userId, deviceId);
        await _userDeviceRepository.AddUserDeviceAsync(UserDevice, cancellationToken);
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

        var otp = OtpGenerator.GenerateOtp();
        await _otpService.CacheAsync(new RegistrationOtpPayload(user!.Id), otp, cancellationToken);
        await _emailService.SendAsync(email, otp, cancellationToken);
        _logger.LogInformation("Confirmation email resent successfully to {Email}", email);

        return Result<SuccessApiResponse>.Success(new SuccessApiResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Confirmation email resent successfully. Please check your email for the new confirmation code.",
        });
    }
    private async Task<Result<SuccessApiResponse>> ValidateResendConfirmationEmailRequestAsync(User? user, CancellationToken cancellationToken)
    {
        if (user == null)
        {
            _logger.LogWarning("Resend confirmation failed: User not found");
            return Result<SuccessApiResponse>.Failure(UserErrors.UserNotFound);
        }
        if (user.IsEmailVerified)
        {
            _logger.LogWarning("Resend confirmation failed: Email {Email} is already confirmed", user.Email);
            return Result<SuccessApiResponse>.Failure(AuthErrors.EmailAlreadyConfirmed);
        }
        return Result<SuccessApiResponse>.Success(default!);
    }
}