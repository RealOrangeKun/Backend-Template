using Application.DTOs.InternalAuth;
using Application.Services.Interfaces;
using Domain.Models.Users;
using Domain.Shared;
using Domain.Enums;
using Domain.Models;
using Application.Constants.ApiErrors;
using Mapster;
using Application.Utils;
using Application.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.Http;

namespace Application.Services.Implementations;

public class InternalAuthService(
        IUserRepository userRepository,
        IEmailService emailService,
        IDistributedCache cache,
        ITokenProvider tokenProvider
    ) : IInternalAuthService
{
    private readonly IUserRepository _userRepository = userRepository;
    private readonly IEmailService _emailService = emailService;
    private readonly IDistributedCache _cache = cache;
    private readonly ITokenProvider _tokenProvider = tokenProvider;
    public async Task<Result<SuccessApiResponse<RegisterResponseDto>>> RegisterAsync(RegisterRequestDto registerRequest, CancellationToken cancellationToken)
    {
        // map using Mapster
        var userCreationParams = registerRequest.Adapt<UserCreationParams>();
        userCreationParams = userCreationParams with { PasswordHash = HashPassword(registerRequest.Password) };
        var user = new User(userCreationParams);
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(30);
        
        // check if the email is taken
        // check if the phone number is taken
        // check if the username is taken
        if (await _userRepository.IsEmailInUseAsync(user.Email, cancellationToken))
        {
            return Result<SuccessApiResponse<RegisterResponseDto>>.Failure(InternalAuthErrors.EmailAlreadyExists);
        }
        if (!string.IsNullOrEmpty(user.PhoneNumber) && await _userRepository.IsPhoneNumberInUseAsync(user.PhoneNumber, cancellationToken))
        {
            return Result<SuccessApiResponse<RegisterResponseDto>>.Failure(InternalAuthErrors.PhoneNumberAlreadyExists);
        }
        if (await _userRepository.IsUsernameInUseAsync(user.Username, cancellationToken))
        {
            return Result<SuccessApiResponse<RegisterResponseDto>>.Failure(InternalAuthErrors.UsernameAlreadyExists);
        }

        // save the user
        await _userRepository.AddUserAsync(user, cancellationToken);

        // Make the Confirmation Token which is a random 6 digits number
        var confirmationToken = new Random().Next(100000, 999999).ToString();

        // Store it in Redis with an expiry of 10 minutes (900 seconds in test)
        // Note: The key is just the user email because the InstanceName "MyBackendTemplate_" is added by the cache provider
        await _cache.SetStringAsync($"{user.Email}", confirmationToken, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        }, cancellationToken);

        // Send the confirmation email
        var emailBody = $"""
            <div style="font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 12px; background-color: #ffffff;">
                <div style="background-color: #4f46e5; color: white; padding: 30px; text-align: center; border-radius: 10px 10px 0 0;">
                    <h1 style="margin: 0; font-size: 24px; font-weight: 700;">Welcome to The Forge</h1>
                </div>
                <div style="padding: 40px; line-height: 1.8; color: #1e293b;">
                    <p style="font-size: 16px; margin-bottom: 24px;">Greetings, traveler!</p>
                    <p style="font-size: 16px; margin-bottom: 24px;">Your journey into the <strong>Backend Odyssey</strong> is about to begin. To verify your identity and unlock your realm, please use the following mystical code:</p>
                    <div style="display: block; width: fit-content; margin: 32px auto; padding: 20px 40px; background-color: #f8fafc; border: 2px dashed #4f46e5; border-radius: 12px; font-size: 32px; font-weight: 800; color: #4f46e5; letter-spacing: 8px; text-align: center; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);">
                        {confirmationToken}
                    </div>
                    <p style="font-size: 14px; color: #64748b; margin-top: 32px;">This code will lose its power in <strong>10 minutes</strong>. If you did not initiate this summoning, you may safely ignore this parchment.</p>
                </div>
                <div style="font-size: 12px; color: #94a3b8; text-align: center; padding: 20px; border-top: 1px solid #f1f5f9;">
                    &copy; 2026 The Architect's Forge. Powered by .NET 9 & Distributed Wisdom.
                </div>
            </div>
            """;

        await _emailService.SendEmailAsync(user.Email, "Activate Your Realm - Verification Code", emailBody, cancellationToken);

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

    private static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt());
    }

    public async Task<Result<SuccessApiResponse<LoginResponseDto>>> LoginAsync(LoginRequestDto loginRequest, CancellationToken cancellationToken)
    {
        // check if user exists by email or username
        var userByEmailAsync = await _userRepository.GetUserByEmailAsync(loginRequest.UsernameOrEmail, cancellationToken);
        var userByUsernameAsync = await _userRepository.GetUserByUsernameAsync(loginRequest.UsernameOrEmail, cancellationToken);
        var user = userByEmailAsync ?? userByUsernameAsync;
        if (user == null)
        {
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(InternalAuthErrors.InvalidCredentials);
        }
        // check if the user is email verified
        if (!user.IsEmailVerified)
        {
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(InternalAuthErrors.EmailNotConfirmed);
        }
        // check if the password is correct
        if (!BCrypt.Net.BCrypt.Verify(loginRequest.Password, user.PasswordHash))
        {
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(InternalAuthErrors.InvalidCredentials);
        }

        // generate a JWT token for the user
        var accessToken = _tokenProvider.GenerateAccessToken(user);

        return Result<SuccessApiResponse<LoginResponseDto>>.Success(new SuccessApiResponse<LoginResponseDto>
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Login successful.",
            Data = new LoginResponseDto
            {
                UserId = user.Id,
                AccessToken = accessToken,
                RefreshToken = user.RefreshToken.ToString()
            }
        });
    }

    public async Task<Result<SuccessApiResponse<RefreshTokenResponseDto>>> RefreshTokenAsync(Guid userId, Guid refreshToken, CancellationToken cancellationToken)
    {
        // Check for missing refresh token (Guid.Empty)
        if (refreshToken == Guid.Empty)
        {
            return Result<SuccessApiResponse<RefreshTokenResponseDto>>.Failure(InternalAuthErrors.MissingRefreshToken);
        }

        // Get the user and check if they exist
        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return Result<SuccessApiResponse<RefreshTokenResponseDto>>.Failure(InternalAuthErrors.UserNotFound);
        }

        // Validate the refresh token and check expiry
        if (user.RefreshToken != refreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
        {
            return Result<SuccessApiResponse<RefreshTokenResponseDto>>.Failure(InternalAuthErrors.InvalidRefreshToken);
        }

        // Generate a new access token
        var newAccessToken = _tokenProvider.GenerateAccessToken(user);

        return Result<SuccessApiResponse<RefreshTokenResponseDto>>.Success(new SuccessApiResponse<RefreshTokenResponseDto>
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Access token refreshed successfully.",
            Data = new RefreshTokenResponseDto
            {
                AccessToken = newAccessToken
            }
        });
    }

    public async Task<Result<SuccessApiResponse>> ForgetPasswordAsync(ForgetPasswordRequestDto forgetPasswordRequest, CancellationToken cancellationToken)
    {
        var email = forgetPasswordRequest.Email;

        // user didn't even register
        if (await _userRepository.IsEmailInUseAsync(email, cancellationToken) == false)
        {
            return Result<SuccessApiResponse>.Failure(InternalAuthErrors.UserNotFound);
        }
        // check if the user is verified
        if (await _userRepository.IsEmailConfirmedAsync(email, cancellationToken) == false)
        {
            return Result<SuccessApiResponse>.Failure(InternalAuthErrors.EmailNotConfirmed);
        }

        // Make the Confirmation Token which is a random 6 digits number
        var confirmationToken = new Random().Next(100000, 999999).ToString();

        // Store it in Redis with an expiry of 10 minutes (900 seconds in test)
        // Note: The key is just the user email because the InstanceName "MyBackendTemplate_" is added by the cache provider
        await _cache.SetStringAsync($"{email}", confirmationToken, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        }, cancellationToken);

        // Send the password reset email
        var emailBody = $"""
            <div style="font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 12px; background-color: #ffffff;">
                <div style="background-color: #4f46e5; color: white; padding: 30px; text-align: center; border-radius: 10px 10px 0 0;">
                    <h1 style="margin: 0; font-size: 24px; font-weight: 700;">Reset Your Password</h1>
                </div>
                <div style="padding: 40px; line-height: 1.8; color: #1e293b;">
                    <p style="font-size: 16px; margin-bottom: 24px;">Hello,</p>
                    <p style="font-size: 16px; margin-bottom: 24px;">We received a request to reset your password. To secure your realm, please use the following reset code:</p>
                    <div style="display: block; width: fit-content; margin: 32px auto; padding: 20px 40px; background-color: #f8fafc; border: 2px dashed #4f46e5; border-radius: 12px; font-size: 32px; font-weight: 800; color: #4f46e5; letter-spacing: 8px; text-align: center; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);">
                        {confirmationToken}
                    </div>
                    <p style="font-size: 14px; color: #64748b; margin-top: 32px;">This code will expire in <strong>10 minutes</strong>. If you did not request this password reset, please ignore this email and your password will remain unchanged.</p>
                </div>
                <div style="font-size: 12px; color: #94a3b8; text-align: center; padding: 20px; border-top: 1px solid #f1f5f9;">
                    &copy; 2026 The Architect's Forge. Powered by .NET 9 & Distributed Wisdom.
                </div>
            </div>
            """;

        await _emailService.SendEmailAsync(email, "Reset Your Password - Recovery Code", emailBody, cancellationToken);

        return Result<SuccessApiResponse>.Success(new SuccessApiResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Password reset email sent successfully. Please check your email for the reset code.",
        });
    }
    public async Task<Result<SuccessApiResponse>> ResetPasswordAsync(ResetPasswordRequestDto resetPasswordRequest, CancellationToken cancellationToken)
    {
         if (await _userRepository.IsEmailInUseAsync(resetPasswordRequest.Email, cancellationToken) == false)
        {
            return Result<SuccessApiResponse>.Failure(InternalAuthErrors.UserNotFound);
        }
        if (await _userRepository.IsEmailConfirmedAsync(resetPasswordRequest.Email, cancellationToken) == false)
        {
            return Result<SuccessApiResponse>.Failure(InternalAuthErrors.EmailNotConfirmed);
        }
        var storedToken = await _cache.GetStringAsync(resetPasswordRequest.Email, cancellationToken);
        if (resetPasswordRequest.Token != storedToken)
        {
            return Result<SuccessApiResponse>.Failure(InternalAuthErrors.InvalidToken);
        }
        // hash the new passord
        var newHashedPassword = HashPassword(resetPasswordRequest.NewPassword);
        // reset it in the database
        await _userRepository.UpdatePasswordByEmailAsync(resetPasswordRequest.Email, newHashedPassword, cancellationToken);
        await _cache.RemoveAsync(resetPasswordRequest.Email, cancellationToken);
        return Result<SuccessApiResponse>.Success(new SuccessApiResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Password reset successful.",
        });
    }
}