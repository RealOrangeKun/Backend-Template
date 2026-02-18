namespace Application.DTOs.Auth;

public record ResetPasswordRequestDto
{
    public string Token { get; init; } = null!;
    public string NewPassword { get; init; } = null!;
}