using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Files.Persistence;

/// <summary>Claim 与删除共享的文件行锁顺序，避免跨连接检查—写入竞态。</summary>
internal static class HostFileRowLocks
{
    public static async Task<bool> TryAcquireAsync(
        IQueryExecutor queryExecutor,
        DatabaseProvider provider,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var statement = provider switch
        {
            DatabaseProvider.SqlServer => HostFileSql.LockHostFileRowSqlServer,
            DatabaseProvider.MySql => HostFileSql.LockHostFileRowMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var lockedId = await queryExecutor
            .QuerySingleOrDefaultAsync<Guid>(
                statement,
                new Dictionary<string, object?> { ["FileId"] = fileId },
                cancellationToken)
            .ConfigureAwait(false);
        return lockedId != Guid.Empty;
    }
}
