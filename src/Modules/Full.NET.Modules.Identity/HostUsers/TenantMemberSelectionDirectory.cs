using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.HostUsers;

/// <summary>从租户成员表解析当前可信 Tenant 的活动成员候选。</summary>
internal sealed class TenantMemberSelectionDirectory(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    ICurrentTenant currentTenant) : ITenantMemberSelectionDirectory
{
    public async Task<PagedResult<TenantUserDirectoryEntry>> ListActiveTenantMembersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = IdentitySqlParameters.Create(
            ("ActiveStatus", TenantMemberStatuses.Active),
            ("Offset", (page - 1) * pageSize),
            ("PageSize", pageSize));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantMembershipSql.CountActiveMemberSelections,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => TenantMembershipSql.ListActiveMemberSelectionsSqlServer,
            DatabaseProvider.MySql => TenantMembershipSql.ListActiveMemberSelectionsMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var records = await queryExecutor.QueryAsync<HostUserDirectoryRecord>(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<TenantUserDirectoryEntry>(
            records.Select(Map).ToArray(),
            page,
            pageSize,
            total);
    }

    public async Task<TenantUserDirectoryEntry?> FindActiveTenantMemberAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var record = await queryExecutor.QuerySingleOrDefaultAsync<HostUserDirectoryRecord>(
                TenantMembershipSql.FindActiveMemberSelectionByUserId,
                IdentitySqlParameters.Create(
                    ("ActiveStatus", TenantMemberStatuses.Active),
                    ("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? null : Map(record);
    }

    public async Task<IReadOnlyDictionary<Guid, TenantUserDirectoryEntry>> FindActiveTenantMembersAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        if (userIds.Count > 100) throw new ArgumentOutOfRangeException(nameof(userIds));
        if (userIds.Count == 0) return new Dictionary<Guid, TenantUserDirectoryEntry>();
        var rows = await queryExecutor.QueryAsync<HostUserDirectoryRecord>(
            TenantMembershipSql.FindActiveMemberSelectionsByUserIds,
            IdentitySqlParameters.Create(("ActiveStatus", TenantMemberStatuses.Active), ("UserIds", userIds.Distinct().ToArray())),
            cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, Map);
    }

    private static TenantUserDirectoryEntry Map(HostUserDirectoryRecord record) =>
        new(record.Id, record.Username, record.DisplayName, record.PreferredLocale);

    private void EnsureTenantContext()
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
        {
            throw new TenantContextMissingException("identity.tenant_context_required");
        }
    }
}
