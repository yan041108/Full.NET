using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Retention;

internal sealed record AuthenticationEventRetentionResult(int Deleted, int Batches)
{
    public static AuthenticationEventRetentionResult Empty { get; } = new(0, 0);
}

/// <summary>每轮有界批量删除过期认证审计；调用者必须设置可信 Host 上下文。</summary>
internal sealed class AuthenticationEventRetentionRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions,
    IOptionsMonitor<AuthenticationEventRetentionOptions> liveOptions)
{
    public async Task<AuthenticationEventRetentionResult> RunOnceAsync(
        AuthenticationEventRetentionOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return AuthenticationEventRetentionResult.Empty;
        }

        var cutoff = clock.UtcNow.AddDays(-options.RetentionDays);
        var deleted = 0;
        var batches = 0;
        while (batches < options.MaxBatchesPerRun && liveOptions.CurrentValue.Enabled)
        {
            var rows = await DeleteBatchAsync(cutoff, options.BatchSize, cancellationToken)
                .ConfigureAwait(false);
            if (rows < 0 || rows > options.BatchSize)
            {
                throw new InvalidOperationException("Authentication event retention exceeded its batch boundary.");
            }

            deleted += rows;
            batches++;
            if (rows < options.BatchSize)
            {
                break;
            }
        }

        return new AuthenticationEventRetentionResult(deleted, batches);
    }

    private Task<int> DeleteBatchAsync(DateTimeOffset cutoff, int batchSize,
        CancellationToken cancellationToken) => databaseOptions.Value.Provider switch
    {
        DatabaseProvider.SqlServer => commandExecutor.ExecuteAsync(
            AuthenticationEventRetentionSql.DeleteSqlServer,
            IdentitySqlParameters.Create(("CutoffUtc", cutoff), ("BatchSize", batchSize)),
            cancellationToken),
        DatabaseProvider.MySql => transaction.ExecuteAsync(async token =>
        {
            var ids = await queryExecutor.QueryAsync<Guid>(
                    AuthenticationEventRetentionSql.SelectIdsMySql,
                    IdentitySqlParameters.Create(("CutoffUtc", cutoff), ("BatchSize", batchSize)),
                    token)
                .ConfigureAwait(false);
            if (ids.Count == 0)
            {
                return 0;
            }

            var claimed = ids.ToArray();
            var rows = await commandExecutor.ExecuteAsync(
                    AuthenticationEventRetentionSql.DeleteIdsMySql,
                    IdentitySqlParameters.Create(("Ids", claimed)), token)
                .ConfigureAwait(false);
            if (rows != claimed.Length)
            {
                throw new InvalidOperationException(
                    "Authentication event retention did not delete every claimed row.");
            }

            return rows;
        }, cancellationToken),
        _ => throw new NotSupportedException("Unsupported database provider.")
    };
}
