namespace Application.DTOs.InternalAuth;

public record ResendConfirmationEmailRequestDto
{
    public string Email { get; init; } = default!;
}