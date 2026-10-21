using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.HostUsers;

/// <summary>Host 范围读取租户活动成员数，供 Tenancy 配额用量基线对账。</summary>
internal sealed class TenantActiveMemberCountPort(IQueryExecutor queryExecutor) : ITenantActiveMemberCountPort
{
    public async Task<IReadOnlyDictionary<Guid, long>> CountActiveMembersByTenantsAsync(
        IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken = default)
    {
        if (tenantIds.Count > 100) throw new ArgumentOutOfRangeException(nameof(tenantIds));
        var ids = tenantIds.Distinct().ToArray();
        var result = ids.ToDictionary(id => id, _ => 0L);
        if (ids.Length == 0) return result;
        var rows = await queryExecutor.QueryAsync<TenantActiveMemberCountRow>(TenantMembershipSql.CountActiveMembersByTenantsForHost,
            IdentitySqlParameters.Create(("TenantIds", ids), ("ActiveStatus", TenantMemberStatuses.Active)), cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) result[row.TenantId] = row.UsageValue;
        return result;
    }

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

/// <summary>权威用量批量聚合的固定列序投影。</summary>
internal sealed record TenantActiveMemberCountRow(Guid TenantId, long UsageValue)
{
    // JIT Dapper 按属性转换 COUNT 的 int/long；Native 仍使用已登记的显式物化器。
    public TenantActiveMemberCountRow() : this(default, default) { }
}
