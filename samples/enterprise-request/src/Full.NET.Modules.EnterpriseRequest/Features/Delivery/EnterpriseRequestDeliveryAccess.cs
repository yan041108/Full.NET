using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.Delivery;

/// <summary>交付入口和 Worker 复用单据查询的数据范围，跨模块权威校验必须在应用事务外执行。</summary>
internal sealed class EnterpriseRequestDeliveryAccess(EnterpriseRequestQueryService queries,
    IBackgroundSessionAuthorization authorization, IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer, ICurrentTenant tenant)
{
    internal async Task<Result<EnterpriseRequestResponse>> ReadAsync(Guid id, SessionBindingSnapshot binding,
        IReadOnlyCollection<string> permissions, CancellationToken token)
    {
        if (tenant.IsHost || tenant.Id is null || binding.TenantId != tenant.Id
            || await authorization.AuthorizeAllAsync(binding, permissions, token).ConfigureAwait(false) is null)
            return Result<EnterpriseRequestResponse>.Failure(new Error("enterprise_request.delivery_forbidden", "The current session is not authorized.", ErrorType.Forbidden));
        // 不把任务创建时的超管声明带入恢复；每次从当前活动角色解析真实数据范围。
        return await queries.GetByIdAsync(id, binding.UserId, false, token).ConfigureAwait(false);
    }

    internal async Task<Result<EnterpriseRequestResponse>> WriteAsync(Guid id, SessionBindingSnapshot binding,
        long version, string permission, CancellationToken token)
    {
        var row = await ReadAsync(id, binding, [EnterpriseRequestPermissions.Read, permission], token).ConfigureAwait(false);
        if (!row.IsSuccess) return row;
        if (row.Value!.Version != version || row.Value.Status != EnterpriseRequestStatusKeys.Draft)
            return Result<EnterpriseRequestResponse>.Failure(new Error("enterprise_request.delivery_version_conflict", "Only the current draft version is writable.", ErrorType.Conflict));
        var allowed = await writeAuthorizer.EnsureCanWriteAsync(tenant.Id!.Value, row.Value.OrganizationUnitId,
            binding.UserId, token).ConfigureAwait(false);
        if (!allowed.IsSuccess) return Result<EnterpriseRequestResponse>.Failure(allowed.Error!);
        return allowed.Value ? row : Result<EnterpriseRequestResponse>.Failure(new Error("enterprise_request.delivery_forbidden", "The request is outside the writable organization scope.", ErrorType.Forbidden));
    }

    internal async Task<bool> AmountsAsync(SessionBindingSnapshot binding, CancellationToken token) =>
        await authorization.AuthorizeAsync(binding, EnterpriseRequestDeliveryPermissions.AmountsRead, token).ConfigureAwait(false) is not null;
}
