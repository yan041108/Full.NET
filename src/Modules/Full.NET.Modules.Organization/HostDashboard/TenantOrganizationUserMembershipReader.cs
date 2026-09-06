using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.HostDashboard;

/// <summary>为通知公告等机构受众场景提供用户隶属只读判断。</summary>
internal sealed class TenantOrganizationUserMembershipReader(IQueryExecutor queryExecutor)
    : ITenantOrganizationUserMembershipReader
{
    /// <inheritdoc />
    public async Task<bool> IsActiveMemberAsync(
        Guid tenantId,
        Guid organizationUnitId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var count = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                OrganizationMembershipSql.CountActiveMembership,
                OrganizationSqlParameters.Create(
                    ("TenantId", tenantId),
                    ("OrganizationUnitId", organizationUnitId),
                    ("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        return count > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantOrganizationMembershipEntry>> ListActiveMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<OrganizationMembershipRecord>(
                OrganizationMembershipSql.ListActiveMembershipsByUser,
                OrganizationSqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        return rows
            .Select(row => new TenantOrganizationMembershipEntry(row.TenantId, row.OrganizationUnitId))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<long> CountDistinctActiveMembersAsync(
        IReadOnlyList<TenantOrganizationMembershipTarget> organizationTargets,
        CancellationToken cancellationToken = default)
    {
        if (organizationTargets.Count == 0)
        {
            return 0;
        }

        var distinctUsers = new HashSet<Guid>();
        foreach (var target in organizationTargets
                     .DistinctBy(item => (item.TenantId, item.OrganizationUnitId)))
        {
            var rows = await queryExecutor.QueryAsync<OrganizationMembershipUserRecord>(
                    OrganizationMembershipSql.ListActiveUsersByTenantAndUnit,
                    OrganizationSqlParameters.Create(
                        ("TenantId", target.TenantId),
                        ("OrganizationUnitId", target.OrganizationUnitId)),
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in rows)
            {
                distinctUsers.Add(row.UserId);
            }
        }

        return distinctUsers.Count;
    }
}

/// <summary>用户机构隶属行投影。</summary>
internal sealed class OrganizationMembershipRecord
{
    public Guid TenantId { get; init; }

    public Guid OrganizationUnitId { get; init; }
}

/// <summary>机构成员用户标识投影。</summary>
internal sealed class OrganizationMembershipUserRecord
{
    public Guid UserId { get; init; }
}
