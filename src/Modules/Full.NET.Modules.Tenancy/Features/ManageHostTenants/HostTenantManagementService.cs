using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Hosting;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Modules.Tenancy.Features.ManageHostTenants;

/// <summary>Host 租户更新与禁用；禁用沿用最后一名活动租户保护。</summary>
internal sealed class HostTenantManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostTenantQueryService tenantQueries,
    IClock clock,
    IFusionCache cache,
    IHostEnvironment environment)
{
    public Task<Result<TenantSummary>> UpdateAsync(
        Guid tenantId,
        UpdateHostTenantRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(tenantId, request, token),
            cancellationToken);

    public Task<Result<TenantSummary>> DisableAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(tenantId, token),
            cancellationToken);

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

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantRecord>(
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
            var stillExists = await queryExecutor.QuerySingleOrDefaultAsync<TenantRecord>(
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

    private async Task<Result<TenantSummary>> DisableCoreAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantRecord>(
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

        await InvalidateTenantCacheAsync(existing, cancellationToken).ConfigureAwait(false);
        return await tenantQueries.GetByIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task InvalidateTenantCacheAsync(
        TenantRecord tenant,
        CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(
                CacheKeyBuilder.TenantResolutionById(
                    environment.EnvironmentName,
                    tenant.Id),
                token: cancellationToken)
            .ConfigureAwait(false);
        await cache.RemoveAsync(
                CacheKeyBuilder.TenantResolutionByDomain(
                    environment.EnvironmentName,
                    tenant.Domain),
                token: cancellationToken)
            .ConfigureAwait(false);
        await cache.RemoveByTagAsync(
                CacheKeyBuilder.TenantTag(tenant.Id),
                token: cancellationToken)
            .ConfigureAwait(false);
        await cache.RemoveByTagAsync(
                CacheKeyBuilder.DomainTag(tenant.Domain),
                token: cancellationToken)
            .ConfigureAwait(false);
    }

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
}
