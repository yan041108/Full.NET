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
/// Disable（禁用）、Enable（重新启用）。并发与安全边界：
/// 1) 所有写操作包裹在 ICommandTransaction 中，按 Version 字段做乐观并发，
///    冲突时读取确认租户仍然存在后返回 VersionConflict，由客户端重读后重试；
/// 2) Disable 内置最后一名活动租户保护（CountActiveTenants &lt;= 1 → LastActiveTenant 拒绝）；
/// 3) 租户准入直接读取权威源，提交后不执行历史缓存清理。
/// 4) Disable 必须在同一事务内写入 TenancyDomainAudit B0 域内审计，审计失败向上抛出让事务回滚。
/// </summary>
internal sealed class HostTenantManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostTenantQueryService tenantQueries,
    TenantHostPackageBinder packageBinder,
    IClock clock,
    ITransactionalDomainAuditWriter<TenancyDomainAuditWrite> domainAuditWriter)
{
    /// <summary>
    /// 更新租户展示名称；要求输入 1~128 长度，按 Version 做乐观并发。准入路径直接观察已提交状态。
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

        return result;
    }

    /// <summary>
    /// 禁用目标租户；若禁用后活动租户数量将降为 0 则返回 LastActiveTenant 错误。
    /// 在事务内写入 B0 域内禁用审计，准入读取权威源。
    /// </summary>
    public async Task<Result<TenantSummary>> DisableAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = await transaction.ExecuteAsync(
                token => DisableCoreAsync(tenantId, token),
                cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// 重新启用已禁用的租户；已活动租户幂等返回当前摘要，并写入 B0 审计。
    /// </summary>
    public async Task<Result<TenantSummary>> EnableAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = await transaction.ExecuteAsync(
                token => EnableCoreAsync(tenantId, token),
                cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// 为租户绑定/解绑计费套餐。要求目标套餐存在且处于 IsActive 状态，null 代表解绑。
    /// 使用 Version 乐观并发；套餐字段随权威读取生效。
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
                TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (request.TenantPackageId is Guid packageId)
        {
            var bindResult = await packageBinder.BindActivePackageAsync(
                    tenantId,
                    packageId,
                    request.Version,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!bindResult.IsSuccess)
            {
                return MapBinderFailure(bindResult.Error!);
            }

            return await tenantQueries.GetByIdAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantSql.AssignHostTenantPackage,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("TenantPackageId", request.TenantPackageId),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            var stillExists = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                    TenantSql.FindById,
                    TenancySqlParameters.Create(("TenantId", tenantId)),
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
                TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantSql.UpdateHostTenantName,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("Name", name),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            var stillExists = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                    TenantSql.FindById,
                    TenancySqlParameters.Create(("TenantId", tenantId)),
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
                TenancySqlParameters.Create(("TenantId", tenantId)),
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
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("UpdatedAtUtc", now)),
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

    private async Task<Result<TenantSummary>> EnableCoreAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById,
                TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (existing.IsActive)
        {
            return await tenantQueries.GetByIdAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantSql.EnableHostTenant,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound();
        }

        await domainAuditWriter.WriteAsync(
                new TenancyDomainAuditWrite(
                    TenancyDomainAuditActionKeys.HostTenantEnable,
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

    private static Result<TenantSummary> MapBinderFailure(Error error) =>
        Result<TenantSummary>.Failure(error);
}
