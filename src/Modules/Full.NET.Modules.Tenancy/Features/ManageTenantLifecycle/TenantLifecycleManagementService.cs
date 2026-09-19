using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantLifecycle;

internal sealed class TenantLifecycleManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostTenantQueryService tenantQueries,
    TenantCommercialReactivateGate commercialReactivateGate,
    IHostUserDirectory users,
    TenantCacheInvalidator cacheInvalidator,
    IClock clock)
{
    public async Task<Result<TenantSummary>> SuspendAsync(
        Guid tenantId,
        SuspendTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await transaction.ExecuteAsync(
                token => SuspendCoreAsync(tenantId, request, token),
                cancellationToken)
            .ConfigureAwait(false);
        await InvalidateIfSuccessAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<Result<TenantSummary>> ReactivateAsync(
        Guid tenantId,
        ReactivateTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await transaction.ExecuteAsync(
                token => ReactivateCoreAsync(tenantId, request, token),
                cancellationToken)
            .ConfigureAwait(false);
        await InvalidateIfSuccessAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<Result<TenantSummary>> CloseAsync(
        Guid tenantId,
        CloseTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await transaction.ExecuteAsync(
                token => CloseCoreAsync(tenantId, request, token),
                cancellationToken)
            .ConfigureAwait(false);
        await InvalidateIfSuccessAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

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

        var result = await transaction.ExecuteAsync(
                token => TransferOwnershipCoreAsync(tenantId, request, token),
                cancellationToken)
            .ConfigureAwait(false);
        await InvalidateIfSuccessAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<Result<TenantSummary>> SuspendCoreAsync(
        Guid tenantId,
        SuspendTenantRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await tenantQueries.GetByIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!tenant.IsSuccess)
        {
            return tenant;
        }

        if (tenant.Value!.LifecycleStatus is TenantLifecycleStatuses.Closed or TenantLifecycleStatuses.Closing)
        {
            return LifecycleStatusInvalid();
        }

        var affected = await commandExecutor.ExecuteAsync(
                TenantSql.UpdateLifecycleStatus,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("LifecycleStatus", TenantLifecycleStatuses.Suspended),
                    ("IsActive", false),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return await CompleteWriteAsync(tenantId, affected, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantSummary>> ReactivateCoreAsync(
        Guid tenantId,
        ReactivateTenantRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await tenantQueries.GetByIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!tenant.IsSuccess)
        {
            return tenant;
        }

        if (tenant.Value!.LifecycleStatus != TenantLifecycleStatuses.Suspended)
        {
            return LifecycleStatusInvalid();
        }

        var commercialGate = await commercialReactivateGate.ValidateAsync(
                tenant.Value!,
                cancellationToken)
            .ConfigureAwait(false);
        if (!commercialGate.IsSuccess)
        {
            return Result<TenantSummary>.Failure(commercialGate.Error!);
        }

        var affected = await commandExecutor.ExecuteAsync(
                TenantSql.UpdateLifecycleStatus,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("LifecycleStatus", TenantLifecycleStatuses.Active),
                    ("IsActive", true),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return await CompleteWriteAsync(tenantId, affected, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantSummary>> CloseCoreAsync(
        Guid tenantId,
        CloseTenantRequest request,
        CancellationToken cancellationToken)
    {
        var activeCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantSql.CountActiveTenants,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (activeCount <= 1)
        {
            return Result<TenantSummary>.Failure(new Error(
                TenancyErrorCodes.LastActiveTenant,
                "The last active tenant cannot be closed.",
                ErrorType.BusinessRule));
        }

        var affected = await commandExecutor.ExecuteAsync(
                TenantSql.UpdateLifecycleStatus,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("LifecycleStatus", TenantLifecycleStatuses.Closed),
                    ("IsActive", false),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return await CompleteWriteAsync(tenantId, affected, cancellationToken)
            .ConfigureAwait(false);
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

    private async Task InvalidateIfSuccessAsync(
        Result<TenantSummary> result,
        CancellationToken cancellationToken)
    {
        if (result.IsSuccess && result.Value is { } tenant)
        {
            await cacheInvalidator.InvalidateAfterCommitAsync(
                    tenant.Id,
                    tenant.Domain,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
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
