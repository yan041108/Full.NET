using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Auditing;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageHostTenants;

/// <summary>
/// Host 侧租户写操作服务。包含三项能力：Update（改名称）、AssignPackage（绑定套餐）、
/// Disable（禁用）。并发与安全边界：
/// 1) 所有写操作包裹在 ICommandTransaction 中，按 Version 字段做乐观并发，
///    冲突时读取确认租户仍然存在后返回 VersionConflict，由客户端重读后重试；
/// 2) Disable 内置最后一名活动租户保护（CountActiveTenants &lt;= 1 → LastActiveTenant 拒绝）；
/// 3) Update/Disable 事务提交成功后立即调用 TenantCacheInvalidator.InvalidateAfterCommitAsync
///    做本地 L1 与共享 L2 失效，AssignPackage 暂不直接失效因租户解析缓存不直接依赖套餐字段。
/// 4) Disable 必须在同一事务内写入 TenancyDomainAudit B0 域内审计，审计失败向上抛出让事务回滚。
/// </summary>
internal sealed class HostTenantManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostTenantQueryService tenantQueries,
    IClock clock,
    TenantCacheInvalidator cacheInvalidator,
    ITransactionalDomainAuditWriter<TenancyDomainAuditWrite> domainAuditWriter)
{
    /// <summary>
    /// 更新租户展示名称；要求输入 1~128 长度，按 Version 做乐观并发。提交后失效缓存。
    /// </summary>
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
            // Expand/Cutover：提交成功后直接失效，不再写入缓存专用 Outbox。
            // 业务已提交：不得把请求取消令牌传给失效路径。
            await cacheInvalidator.InvalidateAfterCommitAsync(
                    tenant.Id,
                    tenant.Domain,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// 禁用目标租户；若禁用后活动租户数量将降为 0 则返回 LastActiveTenant 错误。
    /// 提交后立即失效解析缓存并在事务内写入 B0 域内禁用审计。
    /// </summary>
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
            await cacheInvalidator.InvalidateAfterCommitAsync(
                    tenant.Id,
                    tenant.Domain,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// 为租户绑定/解绑计费套餐。要求目标套餐存在且处于 IsActive 状态，null 代表解绑。
    /// 使用 Version 乐观并发；写成功后不直接触发缓存失效（解析缓存不包含套餐字段）。
    /// </summary>
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

        // B0 域内审计：必须在业务 UPDATE 所在的同一事务内写入，禁用与审计同提交、
        // 同回滚；写入器不开启新事务也不经过 Outbox。写入失败会向上抛出并回滚本次禁用。
        await domainAuditWriter.WriteAsync(
                new TenancyDomainAuditWrite(
                    TenancyDomainAuditActionKeys.HostTenantDisable,
                    tenantId,
                    tenantId,
                    TenancyDomainAuditOutcomes.Success,
                    ActorUserId: null,
                    ActorDisplayName: null,
                    DiffSummaryJson: null),
                cancellationToken)
            .ConfigureAwait(false);

        return await tenantQueries.GetByIdAsync(tenantId, cancellationToken)
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
