using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageHostTenants;

/// <summary>Host 租户更新与禁用；禁用沿用最后一名活动租户保护。</summary>
internal sealed class HostTenantManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IOutboxWriter outboxWriter,
    HostTenantQueryService tenantQueries,
    IClock clock,
    TenantCacheInvalidator cacheInvalidator)
{
    private const string TenantChangedEventType = "fullnet.tenancy.tenant.changed";

    public async Task<Result<TenantSummary>> UpdateAsync(
        Guid tenantId,
        UpdateHostTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await transaction.ExecuteAsync(
                token => UpdateCoreAsync(tenantId, request, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess && result.Value is { } tenant)
        {
            await cacheInvalidator.InvalidateLocalAsync(tenant.Id, tenant.Domain)
                .ConfigureAwait(false);
        }

        return result;
    }

    public async Task<Result<TenantSummary>> DisableAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = await transaction.ExecuteAsync(
                token => DisableCoreAsync(tenantId, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess && result.Value is { } tenant)
        {
            await cacheInvalidator.InvalidateLocalAsync(tenant.Id, tenant.Domain)
                .ConfigureAwait(false);
        }

        return result;
    }

    public Task<Result<TenantSummary>> AssignPackageAsync(
        Guid tenantId,
        AssignHostTenantPackageRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => AssignPackageCoreAsync(tenantId, request, token),
            cancellationToken);

    private async Task<Result<TenantSummary>> AssignPackageCoreAsync(
        Guid tenantId,
        AssignHostTenantPackageRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById,
                new { TenantId = tenantId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (request.TenantPackageId is Guid packageId)
        {
            var package = await queryExecutor.QuerySingleOrDefaultAsync<Features.ManageHostTenantPackages.TenantPackageIdentityRecord>(
                    TenantPackageSql.FindPackageById,
                    new { PackageId = packageId },
                    cancellationToken)
                .ConfigureAwait(false);
            if (package is null)
            {
                return PackageNotFound();
            }

            if (!package.IsActive)
            {
                return PackageInactive();
            }
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantSql.AssignHostTenantPackage,
                new
                {
                    TenantId = tenantId,
                    request.TenantPackageId,
                    UpdatedAtUtc = now,
                    request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            var stillExists = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                    TenantSql.FindById,
                    new { TenantId = tenantId },
                    cancellationToken)
                .ConfigureAwait(false);
            if (stillExists is null)
            {
                return NotFound();
            }

            return VersionConflict();
        }

        return await tenantQueries.GetByIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantSummary>> UpdateCoreAsync(
        Guid tenantId,
        UpdateHostTenantRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 128)
        {
            return Result<TenantSummary>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Tenant name is invalid.",
                ErrorType.Validation));
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById,
                new { TenantId = tenantId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantSql.UpdateHostTenantName,
                new
                {
                    TenantId = tenantId,
                    Name = name,
                    UpdatedAtUtc = now,
                    request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            var stillExists = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                    TenantSql.FindById,
                    new { TenantId = tenantId },
                    cancellationToken)
                .ConfigureAwait(false);
            if (stillExists is null)
            {
                return NotFound();
            }

            return VersionConflict();
        }

        await WriteTenantChangedEventAsync(existing, cancellationToken)
            .ConfigureAwait(false);

        return await tenantQueries.GetByIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantSummary>> DisableCoreAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById,
                new { TenantId = tenantId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (!existing.IsActive)
        {
            return await tenantQueries.GetByIdAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
        }

        var activeCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantSql.CountActiveTenants,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (activeCount <= 1)
        {
            return Result<TenantSummary>.Failure(new Error(
                TenancyErrorCodes.LastActiveTenant,
                "The last active tenant cannot be disabled.",
                ErrorType.BusinessRule));
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantSql.DisableHostTenant,
                new
                {
                    TenantId = tenantId,
                    UpdatedAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound();
        }

        await WriteTenantChangedEventAsync(existing, cancellationToken)
            .ConfigureAwait(false);

        return await tenantQueries.GetByIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task WriteTenantChangedEventAsync(
        TenantResolutionRecord tenant,
        CancellationToken cancellationToken) =>
        outboxWriter.AddAsync(
            TenantChangedEventType,
            1,
            new TenantChangedIntegrationEvent(tenant.Id, tenant.Domain),
            cancellationToken);

    private static Result<TenantSummary> NotFound() =>
        Result<TenantSummary>.Failure(new Error(
            TenancyErrorCodes.NotFound,
            "The tenant was not found.",
            ErrorType.NotFound));

    private static Result<TenantSummary> VersionConflict() =>
        Result<TenantSummary>.Failure(new Error(
            TenancyErrorCodes.VersionConflict,
            "The tenant record was updated concurrently.",
            ErrorType.Conflict));

    private static Result<TenantSummary> PackageNotFound() =>
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
