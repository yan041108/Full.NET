using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Seeding.Dapper;

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
            var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                sql,
                new
                {
                    Resource = ResourceName,
                    TimeoutMilliseconds = _seedOptions.LockTimeoutSeconds * 1000,
                },
                cancellationToken: cancellationToken));
            return result >= 0;
        }

        const string mySql = "SELECT GET_LOCK(@Resource, @TimeoutSeconds);";
        var acquired = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            mySql,
            new
            {
                Resource = ResourceName,
                TimeoutSeconds = _seedOptions.LockTimeoutSeconds,
            },
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
                await connection.ExecuteAsync(new CommandDefinition(
                    releaseSql,
                    new { Resource = ResourceName },
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
