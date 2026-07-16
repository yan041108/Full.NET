using System.Text.RegularExpressions;
using FluentValidation;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

internal sealed partial class ProvisionTenantCommandValidator
    : AbstractValidator<ProvisionTenantCommand>
{
    public ProvisionTenantCommandValidator()
    {
        RuleFor(command => command.Identifier)
            .Must(value => IdentifierPattern().IsMatch(
                value?.Trim().ToLowerInvariant() ?? string.Empty))
            .WithMessage(
                "Identifier must be 3-64 lowercase letters, numbers, or hyphens.");

        RuleFor(command => command.Name)
            .Must(value => !string.IsNullOrWhiteSpace(value)
                && value.Trim().Length <= 128)
            .WithMessage(
                "Name is required and must not exceed 128 characters.");

        RuleFor(command => command.Domain)
            .Must(value => !string.IsNullOrWhiteSpace(value)
                && value.Trim().Length <= 253)
            .WithMessage(
                "Domain is required and must not exceed 253 characters.");
    }

    [GeneratedRegex(
        "^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
