using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Application.Constants.ApiErrors;
using Application.DTOs.Auth;
using Application.DTOs.User;
using Application.Repositories.Interfaces;
using Application.Services.Interfaces;
using Application.Utils;
using Domain.Models.User;
using Domain.Models.UserDevice;
using Domain.Models.UserRefreshTokens;
using Domain.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Application.Services.Implementations.Auth;

public class InternalSessionService(
    IUserRepository userRepository,
    IUserDevicesRepository userDeviceRepository,
    IJwtTokenProvider tokenProvider,
    ILogger<InternalSessionService> logger,
    IDistributedCache cache,
    IEmailService emailService,
    IUserRefreshTokensRepository userRefreshTokensRepository
    ) : IInternalSessionService
{
    private readonly IUserRepository _userRepository = userRepository;
    private readonly IUserDevicesRepository _userDeviceRepository = userDeviceRepository;
    private readonly IJwtTokenProvider _tokenProvider = tokenProvider;
    private readonly ILogger<InternalSessionService> _logger = logger;
    private readonly IDistributedCache _cache = cache;
    private readonly IEmailService _emailService = emailService;
    private readonly IUserRefreshTokensRepository _userRefreshTokensRepository = userRefreshTokensRepository;

    public async Task<Result<SuccessApiResponse<LoginResponseDto>>> LoginAsync(LoginRequestDto loginRequest, IPAddress ipAddress, Guid deviceId, CancellationToken cancellationToken)
    {
        var user = await GetUserByEmailOrUsernameAsync(loginRequest.UsernameOrEmail, cancellationToken);
        var validationResult = await ValidateLoginRequest(user, loginRequest, ipAddress, cancellationToken);
        if (!validationResult.IsSuccess)
        {
            // Only track failed login attempts if user exists (to avoid NullReferenceException)
            if (user != null)
            {
                int attempts = await GetUserLoginAttempts(user, ipAddress, cancellationToken) + 1;
                await IncrementUserLoginAttempts(user, ipAddress, attempts, cancellationToken);
                if (UserShouldBeJailed(attempts))
                {
                    await JailUser(user, ipAddress, cancellationToken);
                }
            }
            _logger.LogWarning("Login failed for user {UsernameOrEmail}: {ErrorMessage}", loginRequest.UsernameOrEmail, validationResult.Error.message);            
            return validationResult;
        }

        if (await DeviceNotRegisteredForUser(user!.Id, deviceId, cancellationToken))
        {
            var confirmationToken = ConfirmationTokenCacheService.GenerateRandomToken();
            await SetNewDeviceConfirmationToken(user.Id, deviceId, confirmationToken, cancellationToken);
            await _emailService.SendNewDeviceConfirmationEmailAsync(user.Email!, confirmationToken, cancellationToken);
            _logger.LogInformation("New device detected for user {UsernameOrEmail}. Confirmation email sent to {Email}", loginRequest.UsernameOrEmail, user.Email);

            return Result<SuccessApiResponse<LoginResponseDto>>.Success(
                new SuccessApiResponse<LoginResponseDto>
                {
                    StatusCode = StatusCodes.Status202Accepted,
                    Message = "New device detected. A confirmation email has been sent to your email address. Please confirm to complete login."
                }
            );
        }

        var refreshToken = GenerateNewRefreshToken();
        var userRefreshToken = new UserRefreshToken(user.Id, HashRefreshToken(refreshToken));
        await _userRefreshTokensRepository.AddUserRefreshTokenAsync(userRefreshToken, cancellationToken);

        var accessToken = _tokenProvider.GenerateAccessToken(user!);
        _logger.LogInformation("User {UsernameOrEmail} logged in successfully", loginRequest.UsernameOrEmail);

        return Result<SuccessApiResponse<LoginResponseDto>>.Success(new SuccessApiResponse<LoginResponseDto>
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Login successful.",
            Data = new LoginResponseDto
            {
                UserId = user!.Id,
                DeviceId = deviceId,
                AccessToken = accessToken,
                RefreshToken = refreshToken
            }
        });
    }
    private async Task<User?> GetUserByEmailOrUsernameAsync(string emailOrUsername, CancellationToken cancellationToken)
    {
        var userByEmailAsync = await _userRepository.GetUserByEmailAsync(emailOrUsername, cancellationToken);
        if (userByEmailAsync != null)
        {
            return userByEmailAsync;
        }
        var userByUsernameAsync = await _userRepository.GetUserByUsernameAsync(emailOrUsername, cancellationToken);
        return userByUsernameAsync;
    }
    private async Task<Result<SuccessApiResponse<LoginResponseDto>>> ValidateLoginRequest(User? user, LoginRequestDto loginRequest, IPAddress ipAddress, CancellationToken cancellationToken)
    {
        if (UserNotFound(user))
        {
            _logger.LogWarning("Login failed: User {UsernameOrEmail} not found", loginRequest.UsernameOrEmail);
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(AuthErrors.InvalidCredentials);
        }
        if (await UserJailed(user!.Id, ipAddress, cancellationToken))
        {
            _logger.LogWarning("Login attempt blocked: User {UsernameOrEmail} is currently jailed for IP {IPAddress}", loginRequest.UsernameOrEmail, ipAddress);
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(AuthErrors.InvalidCredentials);
        }
        if (EmailUnverified(user!))
        {
            _logger.LogWarning("Login failed: Email not verified for user {UsernameOrEmail}", loginRequest.UsernameOrEmail);
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(AuthErrors.InvalidCredentials);
        }

        // Check for null password hash (should not happen for internal auth users, but defensive check)
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            _logger.LogWarning("Login failed: User {UsernameOrEmail} has no password hash", loginRequest.UsernameOrEmail);
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(AuthErrors.InvalidCredentials);
        }
        if (IncorrectPassword(loginRequest.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed: Invalid password for user {UsernameOrEmail}", loginRequest.UsernameOrEmail);
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(AuthErrors.InvalidCredentials);
        }
        return Result<SuccessApiResponse<LoginResponseDto>>.Success(default!);
    }
    private static bool IncorrectPassword(string password, string passwordHash)
    {
        return BCrypt.Net.BCrypt.Verify(password, passwordHash) == false;
    }
    private async Task<bool> UserJailed(Guid userId, IPAddress ipAddress, CancellationToken cancellationToken)
    {
        return await _cache.GetStringAsync($"jail:{userId}:{ipAddress}", cancellationToken) == "true";
    }
    private static bool EmailUnverified(User user)
    {
        if (user.IsEmailVerified == false)
        {
            return true;
        }
        return false;
    }
    private async Task<bool> DeviceNotRegisteredForUser(Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        return await _userDeviceRepository.IsDeviceIdPresentForUserId(deviceId, userId, cancellationToken) == false;
    }
    private async Task<int> GetUserLoginAttempts(User user, IPAddress ipAddress, CancellationToken cancellationToken)
    {
        string key = $"login_attempts:{user.Id}:{ipAddress}";
        var attemptsString = await _cache.GetStringAsync(key, cancellationToken);
        return string.IsNullOrEmpty(attemptsString) ? 0 : int.Parse(attemptsString);
    }
    private async Task IncrementUserLoginAttempts(User user, IPAddress ipAddress, int newAttempts, CancellationToken cancellationToken)
    {
        string key = $"login_attempts:{user.Id}:{ipAddress}";
        await _cache.SetStringAsync(key, newAttempts.ToString(), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20)
        }, cancellationToken);
    }
    private static bool UserShouldBeJailed(int attempts)
    {
        return attempts > 3;
    }
    private async Task JailUser(User user, IPAddress ipAddress, CancellationToken cancellationToken)
    {
        _logger.LogWarning("User {Email} is temporarily jailed due to multiple failed login attempts from IP {IPAddress}", user.Email, ipAddress);
        await _cache.SetStringAsync($"jail:{user!.Id}:{ipAddress}", "true", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
        }, cancellationToken);
    }
    private async Task SetNewDeviceConfirmationToken(Guid userId, Guid deviceId, string confirmationToken, CancellationToken cancellationToken)
    {
        var storedToken = $"new_device:{confirmationToken}";
        var storedValue = $"{userId}:{deviceId}";
        await _cache.SetStringAsync(storedToken, storedValue, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        }, cancellationToken);
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

    public async Task<Result<SuccessApiResponse<LoginResponseDto>>> ConfirmLoginAsync(ConfirmLoginRequestDto confirmLoginRequest, CancellationToken cancellationToken)
    {
        var storedValue = await _cache.GetStringAsync($"new_device:{confirmLoginRequest.Token}", cancellationToken);
        if (NotValidConfirmationToken(storedValue))
        {
            _logger.LogWarning("Login confirmation failed: Invalid or expired token");
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(AuthErrors.InvalidToken);
        }

        var (userId, deviceId) = GetDataFromToken(storedValue!);
        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        if (UserNotFound(user))
        {
            _logger.LogWarning("Login confirmation failed: User not found for token");
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(UserErrors.UserNotFound);
        }

        await _userDeviceRepository.AddUserDeviceAsync(new UserDevice(userId, deviceId), cancellationToken);

        var refreshToken = GenerateNewRefreshToken();
        var userRefreshToken = new UserRefreshToken(user!.Id, HashRefreshToken(refreshToken));
        await _userRefreshTokensRepository.AddUserRefreshTokenAsync(userRefreshToken, cancellationToken);

        var accessToken = _tokenProvider.GenerateAccessToken(user!);
        _logger.LogInformation("Login confirmed successfully for user {UserId}", userId);

        return Result<SuccessApiResponse<LoginResponseDto>>.Success(new SuccessApiResponse<LoginResponseDto>
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Login confirmed successfully.",
            Data = new LoginResponseDto
            {
                UserId = user!.Id,
                DeviceId = deviceId,
                AccessToken = accessToken,
                RefreshToken = refreshToken
            }
        });
    }
    private static bool NotValidConfirmationToken(string? token)
    {
        return string.IsNullOrEmpty(token) || MalformedToken(token);
    }
    private static bool MalformedToken(string storedValue)
    {
        var parts = storedValue.Split(':');
        return parts.Length != 2 || !Guid.TryParse(parts[0], out var userId) || !Guid.TryParse(parts[1], out var deviceId);
    }
    private static (Guid, Guid) GetDataFromToken(string token)
    {
        var parts = token.Split(':');
        return (Guid.Parse(parts[0]), Guid.Parse(parts[1]));
    }

    public async Task<Result<SuccessApiResponse<GuestLoginResponseDto>>> GuestLoginAsync(CancellationToken cancellationToken)
    {
        var user = CreateGuestUser();
        await _userRepository.AddUserAsync(user, cancellationToken);

        var refreshToken = GenerateNewRefreshToken();
        var userRefreshToken = new UserRefreshToken(user.Id, HashRefreshToken(refreshToken));
        await _userRefreshTokensRepository.AddUserRefreshTokenAsync(userRefreshToken, cancellationToken);
        
        var accessToken = _tokenProvider.GenerateAccessToken(user);
        _logger.LogInformation("Guest user created and logged in successfully with user ID {UserId}", user.Id);
        
        return Result<SuccessApiResponse<GuestLoginResponseDto>>.Success(new SuccessApiResponse<GuestLoginResponseDto>
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Guest login successful.",
            Data = new GuestLoginResponseDto
            {
                UserId = user.Id,
                AccessToken = accessToken,
                RefreshToken = refreshToken
            }
        });
    }
    private static User CreateGuestUser()
    {
        return new User(new GuestUserCreationParams());
    }

    public async Task<Result<SuccessApiResponse<RefreshTokenResponseDto>>> RefreshTokenAsync(Guid userId, string refreshTokenFromCookie, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        if (UserNotFound(user))
        {
            return Result<SuccessApiResponse<RefreshTokenResponseDto>>.Failure(UserErrors.UserNotFound);
        }
        var refreshTokenFromDb = 
            await _userRefreshTokensRepository
            .GetUserRefreshTokenAsync(userId, HashRefreshToken(refreshTokenFromCookie), cancellationToken);
        var validationResult = await ValidateRefreshToken(userId, refreshTokenFromCookie, refreshTokenFromDb);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }
        _logger.LogInformation("Refresh token validated successfully for user {UserId}", userId);

        // Mark the old refresh token as used
        await _userRefreshTokensRepository.MarkTokenAsUsedAsync(userId, HashRefreshToken(refreshTokenFromCookie), cancellationToken);

        var newRefreshToken = GenerateNewRefreshToken();
        var userRefreshToken = new UserRefreshToken(user!.Id, HashRefreshToken(newRefreshToken));
        await _userRefreshTokensRepository.AddUserRefreshTokenAsync(userRefreshToken, cancellationToken);

        var newAccessToken = _tokenProvider.GenerateAccessToken(user!);
        _logger.LogInformation("Token refreshed successfully for user {UserId}", userId);

        return Result<SuccessApiResponse<RefreshTokenResponseDto>>.Success(new SuccessApiResponse<RefreshTokenResponseDto>
        {
            StatusCode = StatusCodes.Status200OK,
            Message = "Access token refreshed successfully.",
            Data = new RefreshTokenResponseDto
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken
            }
        });
    }
    private static bool UserNotFound(User? user)
    {
        if (user == null)
        {
            return true;
        }
        return false;
    }
    private async Task<Result<SuccessApiResponse<RefreshTokenResponseDto>>> ValidateRefreshToken(Guid userId, string refreshTokenFromCookie, UserRefreshToken? refreshTokenFromDb)
    {
        if (InvalidRefreshToken(refreshTokenFromDb, refreshTokenFromCookie) || RefreshTokenUsedOutsideGracePeriod(refreshTokenFromDb!))
        {
            _logger.LogWarning("Token refresh failed: Invalid or expired refresh token for user {UserId}", userId);
            return Result<SuccessApiResponse<RefreshTokenResponseDto>>.Failure(AuthErrors.InvalidRefreshToken);
        }
        return Result<SuccessApiResponse<RefreshTokenResponseDto>>.Success(default!);
    }
    private static bool InvalidRefreshToken(UserRefreshToken? userRefreshToken, string refreshTokenFromCookie)
    {
        if (userRefreshToken == null || 
        userRefreshToken.RefreshTokenExpiryTime <= DateTime.UtcNow ||
        userRefreshToken.RefreshTokenHash != HashRefreshToken(refreshTokenFromCookie))
        {
            return true;
        }
        return false;
    }
    private static bool RefreshTokenUsedOutsideGracePeriod(UserRefreshToken userRefreshToken)
    {
        return userRefreshToken.IsUsed && userRefreshToken.UsedAt.HasValue && (DateTime.UtcNow - userRefreshToken.UsedAt.Value).TotalSeconds > 40;
    }
}