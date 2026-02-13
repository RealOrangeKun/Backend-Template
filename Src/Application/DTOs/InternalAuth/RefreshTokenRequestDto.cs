namespace Application.DTOs.InternalAuth;

public record RefreshTokenRequestDto
{
    public Guid UserId { get; init; }
}