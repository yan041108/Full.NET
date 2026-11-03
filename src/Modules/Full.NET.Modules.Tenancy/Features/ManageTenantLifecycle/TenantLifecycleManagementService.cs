using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
using Full.NET.Modules.Tenancy.Persistence;

/// <summary>Tenancy 模块应用服务；编排事务、权限与持久化。</summary>
namespace Full.NET.Modules.Tenancy.Features.ManageTenantLifecycle;

internal sealed class TenantLifecycleManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostTenantQueryService tenantQueries,
    TenantCommercialReactivateGate commercialReactivateGate,
    IHostUserDirectory users,
    IClock clock,
    TenantLifecycleWriteGuard guard)
{
    public Task<Result<TenantSummary>> SuspendAsync(Guid tenantId, SuspendTenantRequest request, CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(token => TransitionAsync(tenantId, request.Version, TenantLifecycleStatuses.Suspended, token), cancellationToken);

    public Task<Result<TenantSummary>> ReactivateAsync(Guid tenantId, ReactivateTenantRequest request, CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(token => TransitionAsync(tenantId, request.Version, TenantLifecycleStatuses.Active, token), cancellationToken);

    public Task<Result<TenantSummary>> CloseAsync(Guid tenantId, CloseTenantRequest request, CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(token => TransitionAsync(tenantId, request.Version, TenantLifecycleStatuses.Closed, token), cancellationToken);

    public async Task<Result<TenantSummary>> TransferOwnershipAsync(
        Guid tenantId,
        TransferTenantOwnershipRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await users.FindActiveHostUserAsync(request.NewOwnerUserId, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return Result<TenantSummary>.Failure(new Error(
                TenancyErrorCodes.OwnerUserNotFound,
                "The new owner user was not found or is inactive.",
                ErrorType.NotFound));
        }

        var result = await transaction.ExecuteResultAsync(
                token => TransferOwnershipCoreAsync(tenantId, request, token),
                cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    // 先取得跨入口门闩，再读权威状态及数量，直到状态写入与结果读取完成才提交。
    private async Task<Result<TenantSummary>> TransitionAsync(Guid tenantId, int version, string target, CancellationToken token)
    {
        if (version <= 0) return Result<TenantSummary>.Failure(new Error(ValidationErrorCodes.Failed, "A positive version is required.", ErrorType.Validation));
        await guard.EnterAsync(token).ConfigureAwait(false);
        var result = await tenantQueries.GetByIdAsync(tenantId, token).ConfigureAwait(false);
        if (!result.IsSuccess) return result;
        var tenant = result.Value!;
        if (tenant.Version != version) return VersionConflict();
        if (tenant.ProvisioningStatus != TenantProvisioningStatuses.Completed) return LifecycleStatusInvalid();
        if (tenant.LifecycleStatus == target && tenant.IsActive == (target == TenantLifecycleStatuses.Active)) return result;
        if (tenant.LifecycleStatus is not (TenantLifecycleStatuses.Active or TenantLifecycleStatuses.Suspended)) return LifecycleStatusInvalid();
        if (target == TenantLifecycleStatuses.Active)
        {
            if (tenant.LifecycleStatus != TenantLifecycleStatuses.Suspended || tenant.ProvisioningStatus != TenantProvisioningStatuses.Completed)
                return LifecycleStatusInvalid();
            var commercial = await commercialReactivateGate.ValidateAsync(tenant, token).ConfigureAwait(false);
            if (!commercial.IsSuccess) return Result<TenantSummary>.Failure(commercial.Error!);
        }
        else
        {
            if (target == TenantLifecycleStatuses.Suspended && tenant.LifecycleStatus != TenantLifecycleStatuses.Active)
                return LifecycleStatusInvalid();
            if (tenant.IsActive && await queryExecutor.QuerySingleOrDefaultAsync<long>(TenantSql.CountActiveTenants,
                cancellationToken: token).ConfigureAwait(false) <= 1)
                return Result<TenantSummary>.Failure(new Error(TenancyErrorCodes.LastActiveTenant,
                    "The last active tenant cannot be stopped.", ErrorType.BusinessRule));
        }
        var affected = await commandExecutor.ExecuteAsync(TenantSql.UpdateLifecycleStatus, TenancySqlParameters.Create(
            ("TenantId", tenantId), ("LifecycleStatus", target), ("IsActive", target == TenantLifecycleStatuses.Active),
            ("UpdatedAtUtc", clock.UtcNow), ("Version", version)), token).ConfigureAwait(false);
        return await CompleteWriteAsync(tenantId, affected, token).ConfigureAwait(false);
    }

    private async Task<Result<TenantSummary>> TransferOwnershipCoreAsync(
        Guid tenantId,
        TransferTenantOwnershipRequest request,
        CancellationToken cancellationToken)
    {
        var affected = await commandExecutor.ExecuteAsync(
                TenantSql.UpdateOwnerUser,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("OwnerUserId", request.NewOwnerUserId),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return await CompleteWriteAsync(tenantId, affected, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantSummary>> CompleteWriteAsync(
        Guid tenantId,
        int affected,
        CancellationToken cancellationToken)
    {
        if (affected != 1)
        {
            var exists = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                    TenantSql.FindById,
                    TenancySqlParameters.Create(("TenantId", tenantId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (exists is null)
            {
                return NotFound();
            }

            return VersionConflict();
        }

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

    private static Result<TenantSummary> LifecycleStatusInvalid() =>
        Result<TenantSummary>.Failure(new Error(
            TenancyErrorCodes.LifecycleStatusInvalid,
            "The tenant lifecycle status does not allow this operation.",
            ErrorType.BusinessRule));
}
