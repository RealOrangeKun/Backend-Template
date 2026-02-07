using Application.DTOs.InternalAuth;

namespace Application.Interfaces;

public interface IAuthService
{
    Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto registerRequest, CancellationToken cancellationToken);
    Task<LoginResponseDto> LoginAsync(LoginRequestDto loginRequest, CancellationToken cancellationToken);
}
