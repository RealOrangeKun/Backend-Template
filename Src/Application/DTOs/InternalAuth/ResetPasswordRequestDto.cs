namespace Application.DTOs.InternalAuth;

public record ResetPasswordRequestDto
{
    public string Email { get; init; } = null!;
    public string Token { get; init; } = null!;
    public string NewPassword { get; init; } = null!;
}