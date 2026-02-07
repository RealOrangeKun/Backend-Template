using Application.DTOs.InternalAuth;

namespace Application.Services;

public class AuthService
{
    public async Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto registerRequest, CancellationToken cancellationToken)
    {

        return new RegisterResponseDto { };
    }

    private static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt());
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto loginRequest, CancellationToken cancellationToken)
    {
        return new LoginResponseDto { };
    }
}
