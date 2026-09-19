using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.PublicRegistrationWays;

/// <summary>匿名公开注册方式列表查询。</summary>
internal sealed class PublicRegistrationWayQueryService(
    IQueryExecutor queryExecutor,
    RegistrationPolicyService policyService,
    IIdentityActiveTenantDirectory activeTenants)
{
    /// <summary>列出指定租户下启用的注册方式；公开注册关闭时返回 403。</summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>公开注册方式列表或稳定业务错误。</returns>
    public async Task<Result<IReadOnlyList<PublicRegistrationWayResponse>>> ListEnabledAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (!await activeTenants.IsActiveTenantAsync(tenantId, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result<IReadOnlyList<PublicRegistrationWayResponse>>.Failure(new Error(
                IdentityErrorCodes.RegistrationWayTenantInactive,
                "The target tenant was not found or is inactive.",
                ErrorType.NotFound));
        }

        var policy = await policyService.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!policy.IsSuccess)
        {
            return Result<IReadOnlyList<PublicRegistrationWayResponse>>.Failure(policy.Error!);
        }

        if (policy.Value!.RegistrationMode == IdentityRegistrationMode.Disabled)
        {
            return Result<IReadOnlyList<PublicRegistrationWayResponse>>.Failure(new Error(
                IdentityErrorCodes.RegistrationDisabled,
                "Registration is disabled.",
                ErrorType.Forbidden));
        }

        if (policy.Value.RegistrationMode != IdentityRegistrationMode.Open)
        {
            return Result<IReadOnlyList<PublicRegistrationWayResponse>>.Failure(new Error(
                IdentityErrorCodes.PublicRegistrationDisabled,
                "Public registration is disabled.",
                ErrorType.Forbidden));
        }

        var rows = await queryExecutor.QueryAsync<RegistrationWayRecord>(
                RegistrationWaySql.ListEnabledByTenant,
                IdentitySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows
            .Select(row => new PublicRegistrationWayResponse(
                row.Id,
                row.Name,
                row.Code,
                row.SortOrder))
            .ToArray();
        return Result<IReadOnlyList<PublicRegistrationWayResponse>>.Success(items);
    }
}
