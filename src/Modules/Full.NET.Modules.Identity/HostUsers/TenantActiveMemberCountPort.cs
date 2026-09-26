using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.HostUsers;

/// <summary>Host 范围读取租户活动成员数，供 Tenancy 配额用量基线对账。</summary>
internal sealed class TenantActiveMemberCountPort(IQueryExecutor queryExecutor) : ITenantActiveMemberCountPort
{
    public async Task<long> CountActiveMembersAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var count = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantMembershipSql.CountActiveMembersByTenantForHost,
                IdentitySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("ActiveStatus", TenantMemberStatuses.Active)),
                cancellationToken)
            .ConfigureAwait(false);
        return count;
    }
}
