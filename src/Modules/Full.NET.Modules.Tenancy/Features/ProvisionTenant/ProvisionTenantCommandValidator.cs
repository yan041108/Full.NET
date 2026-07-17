using System.Text.RegularExpressions;
using FluentValidation;
using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

internal sealed partial class ProvisionTenantCommandValidator
    : AbstractValidator<ProvisionTenantCommand>
{
    public ProvisionTenantCommandValidator()
    {
        RuleFor(command => command.Identifier)
            .Must(value => IdentifierPattern().IsMatch(
                value?.Trim().ToLowerInvariant() ?? string.Empty))
            .WithErrorCode(ValidationErrorCodes.InvalidFormat)
            .WithMessage(
                "Identifier must be 3-64 lowercase letters, numbers, or hyphens.");

        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithErrorCode(ValidationErrorCodes.Required)
            .WithMessage("Name is required and must not exceed 128 characters.")
            .MaximumLength(128)
            .WithErrorCode(ValidationErrorCodes.MaximumLength)
            .WithMessage("Name is required and must not exceed 128 characters.");

        RuleFor(command => command.Domain)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithErrorCode(ValidationErrorCodes.Required)
            .WithMessage("Domain is required and must not exceed 253 characters.")
            .MaximumLength(253)
            .WithErrorCode(ValidationErrorCodes.MaximumLength)
            .WithMessage("Domain is required and must not exceed 253 characters.");
    }

    [GeneratedRegex(
        "^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
