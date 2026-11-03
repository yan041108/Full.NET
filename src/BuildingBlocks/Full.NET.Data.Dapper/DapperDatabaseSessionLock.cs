using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 通过 SQL Server 应用锁或 MySQL 命名锁提供数据库会话级互斥。
/// </summary>
internal sealed class DapperDatabaseSessionLock(
    DbConnectionFactory connectionFactory,
    IOptions<DatabaseOptions> databaseOptions) : IDatabaseSessionLock
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        var connection = connectionFactory.Create();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await TryAcquireProviderLockAsync(
                    connection,
                    _provider,
                    resource,
                    cancellationToken).ConfigureAwait(false))
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            return new LeaseHandle(connection, _provider, resource);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 会话锁必须钉在同一连接上，不能走 <see cref="IQueryExecutor"/>；
    /// 使用 <see cref="DynamicParameters"/> 避免匿名类型进入 Native AOT 执行路径。
    /// </summary>
    internal static DynamicParameters CreateResourceParameters(string resource)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Resource", resource);
        return parameters;
    }

    internal static async Task<bool> TryAcquireProviderLockAsync(
        DbConnection connection,
        DatabaseProvider provider,
        string resource,
        CancellationToken cancellationToken)
    {
        if (provider == DatabaseProvider.SqlServer)
        {
            const string sql =
                """
                DECLARE @Result int;
                EXEC @Result = sys.sp_getapplock
                    @Resource = @Resource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Session',
                    @LockTimeout = 0;
                SELECT @Result;
                """;
            var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                sql,
                CreateResourceParameters(resource),
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return result >= 0;
        }

        const string mySql = "SELECT GET_LOCK(@Resource, 0);";
        var acquired = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            mySql,
            CreateResourceParameters(resource),
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return acquired == 1;
    }

    private sealed class LeaseHandle(
        DbConnection connection,
        DatabaseProvider provider,
        string resource) : IDatabaseSessionLockLease
    {
        private int _disposed;

        public async Task EnsureHeldAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _disposed) != 0 || connection.State != System.Data.ConnectionState.Open)
                throw new InvalidOperationException("数据库控制会话锁已丢失。");
            // 必须查询原连接；禁止用新连接的成功查询代替对旧执行者的持锁证明。
            var sql = provider == DatabaseProvider.SqlServer
                ? "SELECT CASE WHEN APPLOCK_MODE('public', @Resource, 'Session') = 'Exclusive' THEN 1 ELSE 0 END;"
                : "SELECT CASE WHEN IS_USED_LOCK(@Resource) = CONNECTION_ID() THEN 1 ELSE 0 END;";
            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql,
                    CreateResourceParameters(resource), commandTimeout: 5, cancellationToken: cancellationToken)).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("数据库控制会话不再持有原锁。");
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                var releaseSql = provider == DatabaseProvider.SqlServer
                    ? "EXEC sys.sp_releaseapplock @Resource = @Resource, @LockOwner = 'Session';"
                    : "SELECT RELEASE_LOCK(@Resource);";
                await connection.ExecuteAsync(new CommandDefinition(
                    releaseSql,
                    CreateResourceParameters(resource),
                    cancellationToken: CancellationToken.None)).ConfigureAwait(false);
            }
            catch
            {
                // 会话连接关闭时数据库会回收锁；释放路径不能覆盖原业务结果。
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
