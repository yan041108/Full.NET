using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageHostTenants;

internal sealed class TenantHostPackageBinder(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock)
{
    public Task<Result<bool>> BindActivePackageAsync(
        Guid tenantId,
        Guid packageId,
        CancellationToken cancellationToken = default) =>
        BindActivePackageAsync(tenantId, packageId, null, cancellationToken);

    public async Task<Result<bool>> BindActivePackageAsync(
        Guid tenantId,
        Guid packageId,
        int? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var tenant = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById,
                TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            return Result<bool>.Failure(new Error(
                TenancyErrorCodes.NotFound,
                "Tenant was not found.",
                ErrorType.NotFound));
        }

        if (expectedVersion is int version && tenant.Version != version)
        {
            return Result<bool>.Failure(new Error(
                TenancyErrorCodes.VersionConflict,
                "Tenant version conflict.",
                ErrorType.Conflict));
        }

        var package = await queryExecutor.QuerySingleOrDefaultAsync<
                Features.ManageHostTenantPackages.TenantPackageIdentityRecord>(
                TenantPackageSql.FindPackageById,
                TenancySqlParameters.Create(("PackageId", packageId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (package is null)
        {
            return Result<bool>.Failure(new Error(
                TenancyErrorCodes.PackageNotFound,
                "Tenant package was not found.",
                ErrorType.NotFound));
        }

        if (!package.IsActive)
        {
            return Result<bool>.Failure(new Error(
                TenancyErrorCodes.PackageInactive,
                "Tenant package is inactive.",
                ErrorType.BusinessRule));
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantSql.AssignHostTenantPackage,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("TenantPackageId", packageId),
                    ("UpdatedAtUtc", now),
                    ("Version", tenant.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return Result<bool>.Failure(new Error(
                TenancyErrorCodes.VersionConflict,
                "Tenant version conflict.",
                ErrorType.Conflict));
        }

        return Result<bool>.Success(true);
    }
}