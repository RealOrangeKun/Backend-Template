namespace Application.DTOs.InternalAuth;

public record ForgetPasswordRequestDto
{
    public string Email { get; init; } = null!;
}