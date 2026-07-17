using FluentValidation;

namespace Full.NET.Modules.Identity.Features.Login;

internal sealed class LoginCommandValidator : AbstractValidator<Command>
{
    public LoginCommandValidator()
    {
        RuleFor(command => command.Username)
            .NotEmpty()
            .MaximumLength(128);
        RuleFor(command => command.Password)
            .NotEmpty()
            .MaximumLength(1024);
    }
}
