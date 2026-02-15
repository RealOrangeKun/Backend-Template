namespace Application.DTOs.User;

public record RefreshTokenResponseDto
{
    public string AccessToken { get; init; } = null!;
    public string RefreshToken { get; init; } = null!;
}