using FluentValidation;
using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Features.Login;

internal sealed class LoginCommandValidator : AbstractValidator<Command>
{
    public LoginCommandValidator()
    {
        RuleFor(command => command.Username)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithErrorCode(ValidationErrorCodes.Required)
            .WithMessage("Username is required.")
            .MaximumLength(128)
            .WithErrorCode(ValidationErrorCodes.MaximumLength)
            .WithMessage("Username must not exceed {MaxLength} characters.");
        RuleFor(command => command.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithErrorCode(ValidationErrorCodes.Required)
            .WithMessage("Password is required.")
            .MaximumLength(1024)
            .WithErrorCode(ValidationErrorCodes.MaximumLength)
            .WithMessage("Password must not exceed {MaxLength} characters.");
    }
}
