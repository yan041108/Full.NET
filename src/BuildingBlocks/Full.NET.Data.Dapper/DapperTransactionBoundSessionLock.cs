using System.Data;
using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper;

/// <summary>控制锁和命令事务共享物理连接；控制会话断开时数据库同时回滚该连接的事务。</summary>
internal sealed class DapperTransactionBoundSessionLock(DbSession session, IOptions<DatabaseOptions> options)
    : IDatabaseTransactionBoundSessionLock
{
    public async Task<IDatabaseSessionLockLease?> TryAcquireAsync(string resource, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        var connection = await session.PinConnectionAsync(cancellationToken).ConfigureAwait(false);
        bool acquired;
        try
        {
            acquired = await DapperDatabaseSessionLock.TryAcquireProviderLockAsync(connection.Connection, options.Value.Provider, resource, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await session.ReleasePinnedConnectionAsync(connection).ConfigureAwait(false);
            throw;
        }
        if (acquired) return new Lease(session, connection, options.Value.Provider, resource);
        await session.ReleasePinnedConnectionAsync(connection).ConfigureAwait(false);
        return null;
    }

    private sealed class Lease(DbSession session, DbSessionConnectionLease connection, DatabaseProvider provider, string resource)
        : IDatabaseSessionLockLease
    {
        private int _disposed;

        public async Task EnsureHeldAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _disposed) != 0 || connection.Connection.State != ConnectionState.Open)
                throw new InvalidOperationException("数据库控制会话锁已丢失。");
            var sql = provider == DatabaseProvider.SqlServer
                ? "SELECT CASE WHEN APPLOCK_MODE('public', @Resource, 'Session') = 'Exclusive' THEN 1 ELSE 0 END;"
                : "SELECT CASE WHEN IS_USED_LOCK(@Resource) = CONNECTION_ID() THEN 1 ELSE 0 END;";
            if (await connection.Connection.ExecuteScalarAsync<int>(new CommandDefinition(sql,
                    DapperDatabaseSessionLock.CreateResourceParameters(resource), session.Transaction,
                    commandTimeout: 5, cancellationToken: cancellationToken)).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("数据库控制会话不再持有原锁。");
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                if (session.HasTransaction) await session.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                var sql = provider == DatabaseProvider.SqlServer
                    ? "EXEC sys.sp_releaseapplock @Resource = @Resource, @LockOwner = 'Session';"
                    : "SELECT RELEASE_LOCK(@Resource);";
                await connection.Connection.ExecuteAsync(new CommandDefinition(sql,
                    DapperDatabaseSessionLock.CreateResourceParameters(resource), cancellationToken: CancellationToken.None)).ConfigureAwait(false);
            }
            catch
            {
                // 连接断开由数据库回收锁；不能覆盖原业务失败。
            }
            finally { await session.ReleasePinnedConnectionAsync(connection).ConfigureAwait(false); }
        }
    }
}
