namespace Application.DTOs.InternalAuth;

public record RefreshTokenResponseDto
{
    public string AccessToken { get; init; } = null!;
}