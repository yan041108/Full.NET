using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Localization;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Domain;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

internal sealed class Handler(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator)
    : ICommandHandler<ProvisionTenantCommand, TenantSummary>
{
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
                TenancySqlParameters.Create(("Identifier", identifier)),
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
                TenancySqlParameters.Create(("Domain", domain)),
                cancellationToken)
            .ConfigureAwait(false);
        if (domainMatchCount > 0)
        {
            return Conflict(
                TenancyErrorCodes.DomainExists,
                "A tenant with this domain already exists.");
        }

        string? packageCode = null;
        string? packageName = null;
        if (command.TenantPackageId is Guid packageId)
        {
            var package = await queryExecutor.QuerySingleOrDefaultAsync<TenantPackageIdentityRecord>(
                    TenantPackageSql.FindPackageById,
                    TenancySqlParameters.Create(("PackageId", packageId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (package is null)
            {
                return NotFoundPackage();
            }

            if (!package.IsActive)
            {
                return PackageInactive();
            }

            packageCode = package.Code;
            packageName = package.Name;
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
            .ExecuteAsync(
                TenantSql.Insert,
                TenancySqlParameters.Create(
                    ("Id", tenant.Id),
                    ("Identifier", tenant.Identifier),
                    ("Name", tenant.Name),
                    ("Domain", tenant.Domain),
                    ("IsActive", tenant.IsActive),
                    ("CreatedAtUtc", tenant.CreatedAtUtc),
                    ("Version", tenant.Version),
                    ("DefaultLocale", tenant.DefaultLocale),
                    ("TenantPackageId", command.TenantPackageId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Tenant insert affected {affectedRows} rows instead of one.");
        }

        // Expand/Cutover：开通成功后由服务层直接失效缓存；不再写入缓存专用 Outbox。
        // 旧消息类型与兼容 Handler 保留，仅用于排空升级前已入库消息。

        return Result<TenantSummary>.Success(new TenantSummary(
            tenant.Id,
            tenant.Identifier,
            tenant.Name,
            tenant.Domain,
            tenant.IsActive,
            tenant.Version,
            tenant.DefaultLocale,
            command.TenantPackageId,
            packageCode,
            packageName));
    }

    private static Result<TenantSummary> Conflict(string code, string message) =>
        Result<TenantSummary>.Failure(new Error(
            Code: code,
            Message: message,
            Type: ErrorType.Conflict));

    private static Result<TenantSummary> NotFoundPackage() =>
        Result<TenantSummary>.Failure(new Error(
            TenancyErrorCodes.PackageNotFound,
            "The tenant package was not found.",
            ErrorType.NotFound));

    private static Result<TenantSummary> PackageInactive() =>
        Result<TenantSummary>.Failure(new Error(
            TenancyErrorCodes.PackageInactive,
            "The tenant package is not active.",
            ErrorType.BusinessRule));
}
