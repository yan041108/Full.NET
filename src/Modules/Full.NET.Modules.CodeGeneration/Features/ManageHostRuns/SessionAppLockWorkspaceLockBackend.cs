using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 使用 SQL Server sp_getapplock / MySQL GET_LOCK 的会话锁实现跨实例互斥。
/// </summary>
internal sealed class SessionAppLockWorkspaceLockBackend(
    IOptions<DatabaseOptions> databaseOptions) : ICodeGenerationWorkspaceLockBackend
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string lockResource,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockResource);
        var connection = CodeGenerationDbConnectionFactory.Create(
            databaseOptions.Value);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await TryAcquireProviderLockAsync(
                    connection,
                    _provider,
                    lockResource,
                    cancellationToken).ConfigureAwait(false))
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            return new LeaseHandle(connection, _provider, lockResource);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<bool> TryAcquireProviderLockAsync(
        DbConnection connection,
        DatabaseProvider provider,
        string lockResource,
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
                new { Resource = lockResource },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return result >= 0;
        }

        const string mySql = "SELECT GET_LOCK(@Resource, 0);";
        var acquired = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            mySql,
            new { Resource = lockResource },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return acquired == 1;
    }

    private sealed class LeaseHandle(
        DbConnection connection,
        DatabaseProvider provider,
        string lockResource) : IAsyncDisposable
    {
        private int disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
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
                    new { Resource = lockResource },
                    cancellationToken: CancellationToken.None)).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}