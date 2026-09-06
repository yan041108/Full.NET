using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.HostUsers;

/// <summary>为跨模块统计提供 Host 活动用户计数。</summary>
internal sealed class HostActiveUserCountReader(IQueryExecutor queryExecutor)
    : IHostActiveUserCountReader
{
    /// <inheritdoc />
    public Task<long> CountActiveHostUsersAsync(
        CancellationToken cancellationToken = default) =>
        queryExecutor.QuerySingleOrDefaultAsync<long>(
            IdentitySql.CountActiveHostUsers,
            parameters: null,
            cancellationToken);
}
