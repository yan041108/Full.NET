using Microsoft.Extensions.Options;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Abstractions.Time;

/// <summary>Identity 模块应用服务；编排事务、权限与持久化。</summary>
namespace Full.NET.Modules.Identity.Features.ManageMyTenantInvitations;

internal sealed class MyTenantInvitationQueryService(
    IQueryExecutor queryExecutor,
    IActiveTenantContextResolver tenantResolver,
    IClock clock, IOptions<DatabaseOptions>? databaseOptions = null)
{
    public async Task<Result<PagedResult<MyTenantInvitationResponse>>> ListPendingAsync(
        Guid userId,
        CancellationToken cancellationToken = default, int page = 1, int pageSize = 20)
    {
        var profileEmail = await queryExecutor.QuerySingleOrDefaultAsync<string>(
                AccountLifecycleSql.FindUserProfileEmailByUserId,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        var normalizedEmail = AccountChallengeService.NormalizeEmail(profileEmail);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = IdentitySqlParameters.Create(
            ("UserId", userId), ("NormalizedEmail", normalizedEmail), ("PendingStatus", TenantInvitationStatuses.Pending),
            ("NowUtc", clock.UtcNow), ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            TenantMembershipSql.CountPendingInvitationsForInvitee, parameters, cancellationToken).ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<TenantInvitationRecord>(
            databaseOptions?.Value.Provider == DatabaseProvider.MySql
                ? TenantMembershipSql.ListPendingInvitationsPageMySql : TenantMembershipSql.ListPendingInvitationsPageSqlServer,
            parameters, cancellationToken).ConfigureAwait(false);
        // 单次请求内复用目录结果，包括不存在的租户；不跨请求缓存安全事实。
        var tenants = new Dictionary<Guid, TenantContext?>();
        var responses = new List<MyTenantInvitationResponse>(rows.Count);
        foreach (var row in rows)
        {
            if (!tenants.TryGetValue(row.TenantId, out var tenant))
            {
                tenant = await tenantResolver.ResolveActiveByIdAsync(row.TenantId, cancellationToken).ConfigureAwait(false);
                tenants[row.TenantId] = tenant;
            }
            if (tenant is null)
            {
                continue;
            }

            responses.Add(new MyTenantInvitationResponse(
                row.Id,
                row.TenantId,
                tenant.Name,
                row.TargetEmail,
                row.MemberRole,
                row.ExpiresAtUtc,
                row.CreatedAtUtc));
        }

        return Result<PagedResult<MyTenantInvitationResponse>>.Success(new PagedResult<MyTenantInvitationResponse>(responses, page, pageSize, total));
    }
}
