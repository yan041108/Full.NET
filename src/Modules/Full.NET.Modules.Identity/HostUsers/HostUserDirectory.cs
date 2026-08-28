using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.HostUsers;

/// <summary>
/// 为其他模块提供 Host 用户活动校验与批量显示投影。
/// </summary>
internal sealed class HostUserDirectory(IQueryExecutor queryExecutor)
    : IHostUserDirectory,
      IHostUserDisplayDirectory
{
    public async Task<IReadOnlyDictionary<Guid, HostUserDirectoryEntry>> FindHostUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var distinctUserIds = userIds.Distinct().ToArray();
        if (distinctUserIds.Length == 0)
        {
            return new Dictionary<Guid, HostUserDirectoryEntry>();
        }

        var records = await queryExecutor.QueryAsync<HostUserDirectoryRecord>(
                IdentitySql.ListHostUsersByIds,
                IdentitySqlParameters.Create(("UserIds", distinctUserIds)),
                cancellationToken)
            .ConfigureAwait(false);
        return records.ToDictionary(
            record => record.Id,
            record => new HostUserDirectoryEntry(
                record.Id,
                record.Username,
                record.DisplayName));
    }

    public async Task<HostUserDirectoryEntry?> FindActiveHostUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new Dictionary<string, object?> { ["UserId"] = userId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null || !record.IsActive
            ? null
            : new HostUserDirectoryEntry(record.Id, record.Username, record.DisplayName);
    }
}
