using Application.DTOs.Auth;
using Application.DTOs.User;
using Application.Services.Interfaces;
using Domain.Models.User;
using Domain.Shared;
using Domain.Enums;
using Application.Constants.ApiErrors;
using Application.Utils;
using Application.Repositories.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Domain.Extensions;
using Application.Services.Interfaces.Auth.InternalAuth;
using Application.Services.Implementations.Auth.InternalAuth;
using Application.DTOs.Auth.InternalAuth;
using Application.Services.Implementations.Misc;

namespace Application.Services.Implementations;

public class InternalRegisterationService(
        IUserRepository userRepository,
        RegisterationConfirmationEmailSender registrationEmailSender,
        ILogger<InternalRegisterationService> logger,
        IOtpService<RegistrationOtpPayload> _otpService
    ) : IInternalRegisterationService
{
    private readonly IUserRepository _userRepository = userRepository;
    private readonly ILogger<InternalRegisterationService> _logger = logger;
    private readonly RegisterationConfirmationEmailSender _emailService = registrationEmailSender;
    private readonly IOtpService<RegistrationOtpPayload> _otpService = _otpService;

    public async Task<Result<SuccessApiResponse<RegisterResponseDto>>> RegisterAsync(RegisterRequestDto registerRequest, CancellationToken cancellationToken)
    {
        var user = CreateUserForRegisteration(registerRequest);
        var registrationValidationResult = await ValidateRegisterRequestAsync(user, cancellationToken);
        if (!registrationValidationResult.IsSuccess)
        {
            return registrationValidationResult;
        }

        await _userRepository.AddUserAsync(user, cancellationToken);
        _logger.LogInformation("User created successfully with email: {Email}", user.Email);

        string otp = OtpGenerator.GenerateOtp();
        await _otpService.CacheAsync(new RegistrationOtpPayload(user.Id), otp, cancellationToken);
        await _emailService.SendAsync(user.Email!, otp, cancellationToken);
        _logger.LogInformation("Confirmation email sent successfully to {Email}", user.Email);

        return Result<SuccessApiResponse<RegisterResponseDto>>.Success(new SuccessApiResponse<RegisterResponseDto>
        {
            StatusCode = StatusCodes.Status201Created,
            Message = "User registered successfully. Please check your email for the confirmation code.",
            Data = new RegisterResponseDto
            {
                UserId = user.Id
            }
        });
    }
    private static User CreateUserForRegisteration(RegisterRequestDto registerRequest)
    {
        var userCreationParams = new UserCreationParams
        {
            Email = registerRequest.Email,
            Username = registerRequest.Username,
            PhoneNumber = registerRequest.PhoneNumber,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(registerRequest.Password, BCrypt.Net.BCrypt.GenerateSalt()),
            Role = Roles.User
        };
        return new User(userCreationParams);
    }
    private async Task<Result<SuccessApiResponse<RegisterResponseDto>>> ValidateRegisterRequestAsync(User user, CancellationToken cancellationToken)
    {
        if (await IsEmailInUseAsync(user.Email!, cancellationToken))
        {
            _logger.LogWarning("Registration failed: Email {Email} already in use", user.Email);
            return Result<SuccessApiResponse<RegisterResponseDto>>.Failure(UserErrors.EmailAlreadyExists);
        }
        if (await IsUsernameInUseAsync(user.Username!, cancellationToken))
        {
            _logger.LogWarning("Registration failed: Username {Username} already in use", user.Username);
            return Result<SuccessApiResponse<RegisterResponseDto>>.Failure(UserErrors.UsernameAlreadyExists);
        }
        if (!string.IsNullOrEmpty(user.PhoneNumber) && await IsPhoneNumberInUseAsync(user.PhoneNumber, cancellationToken))
        {
            _logger.LogWarning("Registration failed: Phone number {PhoneNumber} already in use", user.PhoneNumber);
            return Result<SuccessApiResponse<RegisterResponseDto>>.Failure(UserErrors.PhoneNumberAlreadyExists);
        }
        return Result<SuccessApiResponse<RegisterResponseDto>>.Success(default!);
    }
    private async Task<bool> IsEmailInUseAsync(string email, CancellationToken cancellationToken)
    {
        return await _userRepository.IsEmailInUseAsync(email, cancellationToken);
    }
    private async Task<bool> IsUsernameInUseAsync(string username, CancellationToken cancellationToken)
    {
        return await _userRepository.IsUsernameInUseAsync(username, cancellationToken);
    }
    private async Task<bool> IsPhoneNumberInUseAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        return await _userRepository.IsPhoneNumberInUseAsync(phoneNumber, cancellationToken);
    }

    public async Task<Result<SuccessApiResponse<RegisterResponseDto>>> GuestPromoteAsync(RegisterRequestDto registerRequest, Guid userId, CancellationToken cancellationToken)
    {
        var user = CreateUserForRegisteration(registerRequest);
        user.SetGuestId(userId);
        var validateGuestPromototionRequestAsyncResult = await ValidateGuestPromoteRequestAsync(user, cancellationToken);
        if (!validateGuestPromototionRequestAsyncResult.IsSuccess)        
        {
            return validateGuestPromototionRequestAsyncResult;
        }

        await _userRepository.UpdateUserAsync(user, cancellationToken);
        _logger.LogInformation("User promoted successfully with email: {Email}", user.Email);

        var otp = OtpGenerator.GenerateOtp();
        await _otpService.CacheAsync(new RegistrationOtpPayload(user.Id), otp, cancellationToken);
        await _emailService.SendAsync(user.Email!, otp, cancellationToken);
        _logger.LogInformation("Confirmation email sent successfully to {Email}", user.Email);

        return Result<SuccessApiResponse<RegisterResponseDto>>.Success(new SuccessApiResponse<RegisterResponseDto>
        {
            StatusCode = StatusCodes.Status201Created,
            Message = "User registered successfully. Please check your email for the confirmation code.",
            Data = new RegisterResponseDto
            {
                UserId = user.Id
            }
        });
    }
    private async Task<Result<SuccessApiResponse<RegisterResponseDto>>> ValidateGuestPromoteRequestAsync(User user, CancellationToken cancellationToken)
    {
        var existingUser = await _userRepository.GetUserByIdAsync(user.Id, cancellationToken);
        if (existingUser == null)
        {
            _logger.LogWarning("Guest promotion failed: User with ID {UserId} not found", user.Id);
            return Result<SuccessApiResponse<RegisterResponseDto>>.Failure(UserErrors.UserNotFound);
        }
        if (existingUser.IsGuest() == false)
        {
            _logger.LogWarning("Guest promotion failed: User with ID {UserId} is not a guest user", user.Id);
            return Result<SuccessApiResponse<RegisterResponseDto>>.Failure(UserErrors.UserIsNotGuest);
        }

        var RegisterRequestValidationResult = await ValidateRegisterRequestAsync(user, cancellationToken);
        if (!RegisterRequestValidationResult.IsSuccess)        
        {
            return RegisterRequestValidationResult;
        }
        return Result<SuccessApiResponse<RegisterResponseDto>>.Success(default!);
    }
}