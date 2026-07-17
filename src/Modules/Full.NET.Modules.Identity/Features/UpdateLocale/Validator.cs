using FluentValidation;
using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Features.UpdateLocale;

internal sealed class Validator : AbstractValidator<Command>
{
    public Validator()
    {
        RuleFor(command => command.Locale)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithErrorCode(ValidationErrorCodes.Required)
            .WithMessage("Locale is required.")
            .MaximumLength(35)
            .WithErrorCode(ValidationErrorCodes.MaximumLength)
            .WithMessage("Locale must not exceed {MaxLength} characters.");
        RuleFor(command => command.ProfileVersion)
            .GreaterThan(0)
            .WithErrorCode(ValidationErrorCodes.InvalidFormat)
            .WithMessage("Profile version must be greater than zero.");
    }
}
