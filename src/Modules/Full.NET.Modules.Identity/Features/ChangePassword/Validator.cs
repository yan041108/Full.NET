using FluentValidation;
using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Features.ChangePassword;

/// <summary>自助改密命令校验器。</summary>
internal sealed class Validator : AbstractValidator<Command>
{
    public Validator()
    {
        RuleFor(command => command.CurrentPassword)
            .NotEmpty()
            .WithErrorCode(ValidationErrorCodes.Required)
            .WithMessage("Current password is required.");
        RuleFor(command => command.NewPassword)
            .NotEmpty()
            .WithErrorCode(ValidationErrorCodes.Required)
            .WithMessage("New password is required.");
    }
}
