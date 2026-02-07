namespace Application.DTOs.InternalAuth;
public record LoginResponseDto
{
    public Guid UserId { get; init; }
    public string AccessToken { get; init; } = null!;
}