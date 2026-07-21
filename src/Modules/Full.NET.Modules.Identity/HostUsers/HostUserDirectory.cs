using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.HostUsers;

/// <summary>
/// 为其他模块提供 Host 用户存在性只读校验。
/// </summary>
internal sealed class HostUserDirectory(IQueryExecutor queryExecutor) : IHostUserDirectory
{
    public async Task<HostUserDirectoryEntry?> FindActiveHostUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null || !record.IsActive
            ? null
            : new HostUserDirectoryEntry(record.Id, record.Username, record.DisplayName);
    }
}
