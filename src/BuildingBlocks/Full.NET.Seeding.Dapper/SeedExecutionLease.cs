using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Seeding.Dapper;

/// <summary>通过数据库提供程序锁实现单飞播种；释放连接时自动解锁。</summary>
internal sealed class SeedExecutionLease(
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<SeedOptions> seedOptions) : ISeedExecutionLeaseProvider
{
    private const string ResourceName = "Full.NET.Seeding";
    private readonly DatabaseOptions _databaseOptions = databaseOptions.Value;
    private readonly SeedOptions _seedOptions = seedOptions.Value;

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        var connection = SeedDbConnectionFactory.Create(_databaseOptions);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var acquired = await AcquireProviderLockAsync(connection, cancellationToken);
            if (!acquired)
            {
                throw new SeedExecutionException(SeedErrorCodes.LockTimeout);
            }

            return new LeaseHandle(connection, _databaseOptions.Provider);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task<bool> AcquireProviderLockAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        if (_databaseOptions.Provider == DatabaseProvider.SqlServer)
        {
            const string sql =
                """
                DECLARE @Result int;
                EXEC @Result = sys.sp_getapplock
                    @Resource = @Resource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Session',
                    @LockTimeout = @TimeoutMilliseconds;
                SELECT @Result;
                """;
            var parameters = new DynamicParameters();
            parameters.Add("Resource", ResourceName);
            parameters.Add("TimeoutMilliseconds", _seedOptions.LockTimeoutSeconds * 1000);
            var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken));
            return result >= 0;
        }

        const string mySql = "SELECT GET_LOCK(@Resource, @TimeoutSeconds);";
        var mySqlParameters = new DynamicParameters();
        mySqlParameters.Add("Resource", ResourceName);
        mySqlParameters.Add("TimeoutSeconds", _seedOptions.LockTimeoutSeconds);
        var acquired = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            mySql,
            mySqlParameters,
            cancellationToken: cancellationToken));
        return acquired == 1;
    }

    private sealed class LeaseHandle(
        System.Data.Common.DbConnection connection,
        DatabaseProvider provider) : IAsyncDisposable
    {
        private int _disposed;

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
                // 释放也必须静态绑定；即使上游失败，原生进程仍应优先主动释放会话锁。
                var parameters = new DynamicParameters();
                parameters.Add("Resource", ResourceName);
                await connection.ExecuteAsync(new CommandDefinition(
                    releaseSql,
                    parameters,
                    cancellationToken: CancellationToken.None));
            }
            catch
            {
                // 连接关闭同样会释放会话锁，释放命令失败不得覆盖原始 Seed 结果。
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
