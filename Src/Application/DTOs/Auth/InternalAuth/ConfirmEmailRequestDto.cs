namespace Application.DTOs.Auth;
public record ConfirmEmailRequestDto
{
    public string Token { get; init; } = default!;
}