using Application.DTOs.InternalAuth;
using FluentValidation;

namespace Application.Validators.InternalAuth;

public class ForgetPasswordRequestDtoValidator : AbstractValidator<ForgetPasswordRequestDto>
{
    public ForgetPasswordRequestDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email format.");
    }
}
