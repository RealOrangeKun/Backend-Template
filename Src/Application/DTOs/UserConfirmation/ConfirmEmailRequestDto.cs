
namespace Application.DTOs.InternalAuth;
public record ConfirmEmailRequestDto
{
    public string Email { get; init; } = default!;
    public string Token { get; init; } = default!;
}