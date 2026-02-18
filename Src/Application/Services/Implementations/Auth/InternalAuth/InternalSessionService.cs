using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Application.Constants.ApiErrors;
using Application.DTOs.Auth;
using Application.DTOs.Auth.InternalAuth;
using Application.DTOs.User;
using Application.Repositories.Interfaces;
using Application.Services.Implementations.Misc;
using Application.Services.Interfaces;
using Application.Services.Interfaces.Auth;
using Application.Services.Interfaces.Auth.InternalAuth;
using Application.Utils;
using Domain.Extensions;
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
    IRefreshTokenProvider RefreshTokenProvider,
    JwtTokenProvider jwtTokenProvider,
    ILogger<InternalSessionService> logger,
    ILoginThrottlingService loginThrottlingService,
    IUserRefreshTokensRepository userRefreshTokensRepository,
    IOtpService<NewDeviceOtpPayload> otpService,
    NewDeviceConfirmationEmailSender emailService
    ) : IInternalSessionService
{
    private readonly IUserRepository _userRepository = userRepository;
    private readonly IUserDevicesRepository _userDeviceRepository = userDeviceRepository;
    private readonly IRefreshTokenProvider _refreshTokenProvider = RefreshTokenProvider;
    private readonly JwtTokenProvider _jwtTokenProvider = jwtTokenProvider;
    private readonly ILogger<InternalSessionService> _logger = logger;
    private readonly ILoginThrottlingService _loginThrottlingService = loginThrottlingService;
    private readonly IUserRefreshTokensRepository _userRefreshTokensRepository = userRefreshTokensRepository;
    private readonly IOtpService<NewDeviceOtpPayload> _otpService = otpService;
    private readonly NewDeviceConfirmationEmailSender _emailService = emailService;

    public async Task<Result<SuccessApiResponse<LoginResponseDto>>> LoginAsync(LoginRequestDto loginRequest, IPAddress ipAddress, Guid deviceId, CancellationToken cancellationToken)
    {
        var user = await GetUserByEmailOrUsernameAsync(loginRequest.UsernameOrEmail, cancellationToken);
        var validationResult = await ValidateLoginRequest(user, loginRequest, ipAddress, cancellationToken);
        if (!validationResult.IsSuccess)
        {
            if (user != null)
            {
                int attempts = await _loginThrottlingService.GetUserLoginAttempts(user, ipAddress, cancellationToken) + 1;
                await _loginThrottlingService.IncrementUserLoginAttempts(user, ipAddress, attempts, cancellationToken);
                if (_loginThrottlingService.ShouldBeJailed(attempts))
                {
                    await _loginThrottlingService.JailUser(user, ipAddress, cancellationToken);
                    _logger.LogWarning("User {UsernameOrEmail} has been jailed due to too many failed login attempts from IP {IPAddress}", loginRequest.UsernameOrEmail, ipAddress);
                }
            }
            _logger.LogWarning("Login failed for user {UsernameOrEmail}: {ErrorMessage}", loginRequest.UsernameOrEmail, validationResult.Error.message);            
            return validationResult;
        }

        if (await IsDeviceNotTrustedAsync(user!.Id, deviceId, cancellationToken))
        {
            _logger.LogInformation("New device detected for user {UsernameOrEmail}. Confirmation email sent to {Email}", loginRequest.UsernameOrEmail, user.Email);
            return await RegisterNewDevice(user, deviceId, cancellationToken);
        }

        var refreshToken = _refreshTokenProvider.GenerateNewRefreshToken();
        await _userRefreshTokensRepository
            .AddUserRefreshTokenAsync(new UserRefreshToken(user.Id, _refreshTokenProvider.HashRefreshToken(refreshToken)), cancellationToken);

        var accessToken = _jwtTokenProvider.GenerateAccessToken(user!);
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
        if (user == null)
        {
            _logger.LogWarning("Login failed: User {UsernameOrEmail} not found", loginRequest.UsernameOrEmail);
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(AuthErrors.InvalidCredentials);
        }
        if (await _loginThrottlingService.IsUserJailed(user!.Id, ipAddress, cancellationToken))
        {
            _logger.LogWarning("Login attempt blocked: User {UsernameOrEmail} is currently jailed for IP {IPAddress}", loginRequest.UsernameOrEmail, ipAddress);
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(AuthErrors.InvalidCredentials);
        }
        if (user.IsNotEmailVerified())
        {
            _logger.LogWarning("Login failed: Email not verified for user {UsernameOrEmail}", loginRequest.UsernameOrEmail);
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(AuthErrors.InvalidCredentials);
        }
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            _logger.LogWarning("Login failed: User {UsernameOrEmail} has no password hash", loginRequest.UsernameOrEmail);
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(AuthErrors.InvalidCredentials);
        }
        if (user.IncorrectPassword(loginRequest.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed: Invalid password for user {UsernameOrEmail}", loginRequest.UsernameOrEmail);
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(AuthErrors.InvalidCredentials);
        }
        return Result<SuccessApiResponse<LoginResponseDto>>.Success(default!);
    }
    private async Task<bool> IsDeviceNotTrustedAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        return await _userDeviceRepository.IsDeviceIdPresentForUserId(deviceId, userId, cancellationToken) == false;
    }
    private async Task<Result<SuccessApiResponse<LoginResponseDto>>> RegisterNewDevice(User user, Guid deviceId, CancellationToken cancellationToken)
    {
        var otp = OtpGenerator.GenerateOtp();
        await _otpService.CacheAsync(new NewDeviceOtpPayload(user.Id, deviceId), otp, cancellationToken);
        await _emailService.SendAsync(user.Email!, otp, cancellationToken);

        return Result<SuccessApiResponse<LoginResponseDto>>.Success(
            new SuccessApiResponse<LoginResponseDto>
            {
                StatusCode = StatusCodes.Status202Accepted,
                Message = "New device detected. A confirmation email has been sent to your email address. Please confirm to complete login."
            }
        );
    }

    public async Task<Result<SuccessApiResponse<LoginResponseDto>>> ConfirmLoginForNewDeviceAsync(ConfirmLoginRequestDto confirmLoginRequest, CancellationToken cancellationToken)
    {
        var storedValue = await _otpService.GetDataAsync(confirmLoginRequest.Otp, cancellationToken);
        var userId = storedValue?.UserId ?? Guid.Empty;
        var deviceId = storedValue?.DeviceId ?? Guid.Empty;
        if (IsNotValidConfirmationToken(userId, deviceId))
        {
            _logger.LogWarning("Login confirmation failed: Invalid or expired token");
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(AuthErrors.InvalidToken);
        }
        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Login confirmation failed: User not found for token");
            return Result<SuccessApiResponse<LoginResponseDto>>.Failure(UserErrors.UserNotFound);
        }

        await _userDeviceRepository.AddUserDeviceAsync(new UserDevice(userId, deviceId), cancellationToken);

        var refreshToken = _refreshTokenProvider.GenerateNewRefreshToken();
        await _userRefreshTokensRepository
            .AddUserRefreshTokenAsync(new UserRefreshToken(user!.Id, _refreshTokenProvider.HashRefreshToken(refreshToken)), cancellationToken);

        var accessToken = _jwtTokenProvider.GenerateAccessToken(user!);
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
    private static bool IsNotValidConfirmationToken(Guid userId, Guid deviceId)
    {
        return userId == Guid.Empty || deviceId == Guid.Empty;
    }

    public async Task<Result<SuccessApiResponse<GuestLoginResponseDto>>> GuestLoginAsync(CancellationToken cancellationToken)
    {
        var user = CreateGuestUser();
        await _userRepository.AddUserAsync(user, cancellationToken);

        var refreshToken = _refreshTokenProvider.GenerateNewRefreshToken();
        var userRefreshToken = new UserRefreshToken(user.Id, _refreshTokenProvider.HashRefreshToken(refreshToken));
        await _userRefreshTokensRepository.AddUserRefreshTokenAsync(userRefreshToken, cancellationToken);
        
        var accessToken = _jwtTokenProvider.GenerateAccessToken(user);
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
        if (user == null)
        {
            _logger.LogWarning("Refresh token failed: User not found for ID {UserId}", userId);
            return Result<SuccessApiResponse<RefreshTokenResponseDto>>.Failure(UserErrors.UserNotFound);
        }
        var refreshTokenFromDb = 
            await _userRefreshTokensRepository
            .GetUserRefreshTokenAsync(userId, _refreshTokenProvider.HashRefreshToken(refreshTokenFromCookie), cancellationToken);
        var validationResult = _refreshTokenProvider.IsInvalidRefreshToken(refreshTokenFromDb, refreshTokenFromCookie);
        if (validationResult)
        {
            return Result<SuccessApiResponse<RefreshTokenResponseDto>>.Failure(AuthErrors.InvalidRefreshToken);
        }

        await _userRefreshTokensRepository.MarkTokenAsUsedAsync(userId, _refreshTokenProvider.HashRefreshToken(refreshTokenFromCookie), cancellationToken);

        var newRefreshToken = _refreshTokenProvider.GenerateNewRefreshToken();
        await _userRefreshTokensRepository
            .AddUserRefreshTokenAsync(new UserRefreshToken(user!.Id, _refreshTokenProvider.HashRefreshToken(newRefreshToken)), cancellationToken);

        var newAccessToken = _jwtTokenProvider.GenerateAccessToken(user!);
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
}