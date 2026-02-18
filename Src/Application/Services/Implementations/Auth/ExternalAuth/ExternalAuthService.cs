using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Application.Constants.ApiErrors;
using Application.DTOs.ExternalAuth;
using Application.DTOs.User;
using Application.Repositories.Interfaces;
using Application.Services.Interfaces;
using Application.Utils;
using Domain.Enums;
using Domain.Models;
using Domain.Models.User;
using Domain.Models.UserRefreshTokens;
using Domain.Shared;
using Google.Apis.Auth;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Services.Implementations;

public class ExternalAuthService(
    IUserRepository userRepo,
    IJwtTokenProvider tokenProvider, 
    IConfiguration configuration,
    IGoogleAuthValidator googleAuthValidator,
    IUserRefreshTokensRepository userRefreshTokensRepository,
    ILogger<ExternalAuthService> logger) 
    : IExternalAuthService
{
    private readonly IUserRepository _userRepository = userRepo;
    private readonly IUserRefreshTokensRepository _userRefreshTokensRepository = userRefreshTokensRepository;
    private readonly IJwtTokenProvider _tokenProvider = tokenProvider;
    private readonly IConfiguration _configuration = configuration;
    private readonly IGoogleAuthValidator _googleAuthValidator = googleAuthValidator;
    private readonly ILogger<ExternalAuthService> _logger = logger;

    public async Task<Result<SuccessApiResponse<GoogleAuthResponseDto>>> GoogleLoginAsync(GoogleAuthRequestDto authRequest, CancellationToken cancellationToken)
    {        
        var payloadResult = await ValidateAndGetGooglePayloadAsync(authRequest.IdToken);
        if (!payloadResult.IsSuccess)
        {
            return Result<SuccessApiResponse<GoogleAuthResponseDto>>.Failure(payloadResult.Error);
        }
        var payload = payloadResult.Data;
        
        var user = await _userRepository.GetUserByGoogleIdAsync(payload.Subject, cancellationToken);
        if (UserNotFound(user)) // not found by google id
        {
            user = await _userRepository.GetUserByEmailAsync(payload.Email, cancellationToken);
            if (UserNotFound(user)) // not found by email
            {
                user = await CreateExternalUserAsync(payload, cancellationToken);
            }
            else // found by email but not linked to google, link the google account
            {
                user = await UpdateUserToBeEligibleForExternalLogin(user!, payload, cancellationToken);
            }
        }

        var refreshToken = GenerateNewRefreshToken();
        var userRefreshToken = new UserRefreshToken(user!.Id, HashRefreshToken(refreshToken));
        await _userRefreshTokensRepository.AddUserRefreshTokenAsync(userRefreshToken, cancellationToken);

        var accessToken = _tokenProvider.GenerateAccessToken(user);
        _logger.LogInformation("Google login successful for user {UserId}", user.Id);

        return Result<SuccessApiResponse<GoogleAuthResponseDto>>.Success(new SuccessApiResponse<GoogleAuthResponseDto>
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Google authentication successful.",
            Data = new GoogleAuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                UserId = user.Id
            }
        });
    }
    private static bool UserNotFound(User? user)
    {
        return user == null;
    }
    private async Task<Result<GoogleJsonWebSignature.Payload>> ValidateAndGetGooglePayloadAsync(string idToken)
    {
        var googleClientId = _configuration["Google:ClientId"];
        _logger.LogInformation("Validating Google token against ClientId: {ClientId}", googleClientId ?? "NULL");
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [googleClientId] 
            };
            
            var payload = await _googleAuthValidator.ValidateAsync(idToken, settings);
            _logger.LogInformation("Google token validated for email: {Email}", payload.Email);
            return Result<GoogleJsonWebSignature.Payload>.Success(payload);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Google token validation failed");
            return Result<GoogleJsonWebSignature.Payload>.Failure(AuthErrors.InvalidCredentials);
        }
    }
    private async Task<User> CreateExternalUserAsync(GoogleJsonWebSignature.Payload payload, CancellationToken ct)
    {
        _logger.LogInformation("Creating new external user for email: {Email}", payload.Email);
        
        var userCreationParams = new ExternalUserCreationParams
        {
            Email = payload.Email,
            GoogleId = payload.Subject,
            Role = Roles.User,
        };

        var user = new User(userCreationParams);
        user.MarkEmailAsVerified();

        await _userRepository.AddUserAsync(user, ct);
        return user;
    }
    private static string GenerateNewRefreshToken()
    {
        // 32 bytes of randomness provides 256 bits of entropy
        var randomNumber = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        
        // Convert to Base64 to make it a URL-safe string for the user
        return Convert.ToBase64String(randomNumber);
    }
    private static string HashRefreshToken(string refreshToken)
    {
        var inputBytes = Encoding.UTF8.GetBytes(refreshToken);
        var hashBytes = SHA256.HashData(inputBytes);

        // Convert the byte array to a lowercase 64-character hexadecimal string
        return Convert.ToHexString(hashBytes).ToLower();
    }

    public async Task<Result<SuccessApiResponse<GoogleAuthResponseDto>>> LinkGoogleAccountAsync(GoogleAuthRequestDto authRequest, Guid userId, CancellationToken cancellationToken)
    {        
        var payloadResult = await ValidateAndGetGooglePayloadAsync(authRequest.IdToken);
        if (!payloadResult.IsSuccess)
        {
            return Result<SuccessApiResponse<GoogleAuthResponseDto>>.Failure(payloadResult.Error);
        }
        var payload = payloadResult.Data;

        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        var validationResult = await ValidateGoogleAccountLinkingAsync(payload, user, cancellationToken);
        if (!validationResult.IsSuccess)
        {
            return Result<SuccessApiResponse<GoogleAuthResponseDto>>.Failure(validationResult.Error);
        }

        await UpdateUserToBeEligibleForExternalLogin(user!, payload, cancellationToken);

        var refreshToken = GenerateNewRefreshToken();
        var userRefreshToken = new UserRefreshToken(user!.Id, HashRefreshToken(refreshToken));
        await _userRefreshTokensRepository.AddUserRefreshTokenAsync(userRefreshToken, cancellationToken);

        var accessToken = _tokenProvider.GenerateAccessToken(user!);
        _logger.LogInformation("Google account linked successfully for user {UserId}", user!.Id);

        return Result<SuccessApiResponse<GoogleAuthResponseDto>>.Success(new SuccessApiResponse<GoogleAuthResponseDto>
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Google authentication successful.",
            Data = new GoogleAuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                UserId = user.Id
            }
        });
    }
    private async Task<Result<User>> ValidateGoogleAccountLinkingAsync(GoogleJsonWebSignature.Payload payload, User? user, CancellationToken ct)
    {
        if (UserNotFound(user))
        {
            _logger.LogWarning("Cannot link Google account: User not found");
            return Result<User>.Failure(UserErrors.UserNotFound);
        }
        if (NotGuestUser(user!))
        {
            _logger.LogWarning("Cannot link Google account: User {UserId} is not a guest user", user!.Id);
            return Result<User>.Failure(UserErrors.UserIsNotGuest);
        }
        if (await IsEmailInUseAsync(payload.Email, ct))
        {
            _logger.LogWarning("Cannot link Google account: Email {Email} is already in use", payload.Email);
            return Result<User>.Failure(UserErrors.EmailAlreadyExists);
        }
        return Result<User>.Success(user!);
    }
    private static bool NotGuestUser(User user)
    {
        return !user.IsGuest();
    }
    private async Task<bool> IsEmailInUseAsync(string email, CancellationToken cancellationToken)
    {
        return await _userRepository.IsEmailInUseAsync(email, cancellationToken);
    } 
    private async Task<User> UpdateUserToBeEligibleForExternalLogin(User user, GoogleJsonWebSignature.Payload payload, CancellationToken ct)
    {
        user.UpdateUserToBeEligibleForExternalLogin(payload.Email, payload.Subject);
        await _userRepository.UpdateUserAsync(user, ct);
        return user;
    }
}