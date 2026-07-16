using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Domain;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

internal sealed partial class Handler(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IOutboxWriter outboxWriter,
    IClock clock,
    IIdGenerator idGenerator)
    : ICommandHandler<ProvisionTenantCommand, TenantSummary>
{
    private const string EventType = "fullnet.tenancy.tenant-provisioned";

    public async Task<Result<TenantSummary>> HandleAsync(
        ProvisionTenantCommand command,
        CancellationToken cancellationToken)
    {
        var identifier = command.Identifier?.Trim().ToLowerInvariant() ?? string.Empty;
        var name = command.Name?.Trim() ?? string.Empty;
        var domain = command.Domain?.Trim().ToLowerInvariant() ?? string.Empty;

        var validationErrors = Validate(identifier, name, domain);
        if (validationErrors.Count > 0)
        {
            return Result<TenantSummary>.Failure(new Error(
                "tenancy.validation",
                "Tenant details are invalid.",
                ErrorType.Validation,
                validationErrors));
        }

        var identifierMatchCount = await queryExecutor
            .QuerySingleOrDefaultAsync<long>(
                TenantSql.FindByIdentifier,
                new { Identifier = identifier },
                cancellationToken)
            .ConfigureAwait(false);
        if (identifierMatchCount > 0)
        {
            return Conflict(
                "tenancy.identifier-exists",
                "A tenant with this identifier already exists.");
        }

        var domainMatchCount = await queryExecutor
            .QuerySingleOrDefaultAsync<long>(
                TenantSql.CountByDomain,
                new { Domain = domain },
                cancellationToken)
            .ConfigureAwait(false);
        if (domainMatchCount > 0)
        {
            return Conflict(
                "tenancy.domain-exists",
                "A tenant with this domain already exists.");
        }

        var tenant = new Tenant(
            idGenerator.NewId(),
            identifier,
            name,
            domain,
            true,
            clock.UtcNow,
            1);
        var affectedRows = await commandExecutor
            .ExecuteAsync(TenantSql.Insert, tenant, cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Tenant insert affected {affectedRows} rows instead of one.");
        }

        await outboxWriter.AddAsync(
                EventType,
                1,
                new TenantProvisionedIntegrationEvent(
                    tenant.Id,
                    tenant.Identifier,
                    tenant.Domain),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<TenantSummary>.Success(new TenantSummary(
            tenant.Id,
            tenant.Identifier,
            tenant.Name,
            tenant.Domain,
            tenant.IsActive,
            tenant.Version));
    }

    private static Dictionary<string, string[]> Validate(
        string identifier,
        string name,
        string domain)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!IdentifierPattern().IsMatch(identifier))
        {
            errors[nameof(ProvisionTenantCommand.Identifier)] =
                ["Identifier must be 3-64 lowercase letters, numbers, or hyphens."];
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
        {
            errors[nameof(ProvisionTenantCommand.Name)] =
                ["Name is required and must not exceed 128 characters."];
        }

        if (string.IsNullOrWhiteSpace(domain) || domain.Length > 255)
        {
            errors[nameof(ProvisionTenantCommand.Domain)] =
                ["Domain is required and must not exceed 255 characters."];
        }

        return errors;
    }

    private static Result<TenantSummary> Conflict(string code, string message) =>
        Result<TenantSummary>.Failure(new Error(
            code,
            message,
            ErrorType.Conflict));

    [GeneratedRegex(
        "^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
