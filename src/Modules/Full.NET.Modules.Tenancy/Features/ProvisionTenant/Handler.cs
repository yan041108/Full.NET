using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Domain;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Localization;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

internal sealed class Handler(
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

        var identifierMatchCount = await queryExecutor
            .QuerySingleOrDefaultAsync<long>(
                TenantSql.FindByIdentifier,
                new { Identifier = identifier },
                cancellationToken)
            .ConfigureAwait(false);
        if (identifierMatchCount > 0)
        {
            return Conflict(
                TenancyErrorCodes.IdentifierExists,
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
                TenancyErrorCodes.DomainExists,
                "A tenant with this domain already exists.");
        }

        var tenant = new Tenant(
            idGenerator.NewId(),
            identifier,
            name,
            domain,
            true,
            clock.UtcNow,
            1,
            LocaleCatalog.DefaultLocale);
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
            tenant.Version,
            tenant.DefaultLocale));
    }

    private static Result<TenantSummary> Conflict(string code, string message) =>
        Result<TenantSummary>.Failure(new Error(
            Code: code,
            Message: message,
            Type: ErrorType.Conflict));
}
