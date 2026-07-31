using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Reconciliation;

internal sealed record PendingHostFileReconciliationResult(
    int Scanned,
    int Promoted,
    int Purged,
    int ProbeFailures,
    int RetainedPublishing,
    int ConcurrentlyCompleted,
    int BatchesExecuted)
{
    public static PendingHostFileReconciliationResult Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0);
}

internal sealed class PendingHostFileReconciliationRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    FileStorageProviderRegistry storageProviders,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock)
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;

    public async Task<PendingHostFileReconciliationResult> RunOnceAsync(
        PendingHostFileReconciliationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return PendingHostFileReconciliationResult.Empty;
        }

        var createdBeforeUtc = clock.UtcNow.AddSeconds(-options.MinimumAgeSeconds);
        var scanned = 0;
        var promoted = 0;
        var purged = 0;
        var probeFailures = 0;
        var retainedPublishing = 0;
        var concurrentlyCompleted = 0;
        var batches = 0;
        var hasCursor = false;
        var afterCreatedAtUtc = DateTimeOffset.UnixEpoch;
        Guid? afterId = null;

        while (batches < options.MaxBatchesPerRun)
        {
            var records = await queryExecutor.QueryAsync<PendingHostFileRecord>(
                    SelectStatement(),
                    new
                    {
                        CreatedBeforeUtc = createdBeforeUtc,
                        HasCursor = hasCursor ? 1 : 0,
                        AfterCreatedAtUtc = afterCreatedAtUtc,
                        AfterId = afterId,
                        options.BatchSize,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            batches++;
            if (records.Count == 0)
            {
                break;
            }

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;
                bool exists;
                try
                {
                    var storageProvider = storageProviders.Resolve(record.ProviderKey);
                    exists = await storageProvider.ExistsAsync(
                            record.StorageKey,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // 未知或不可用的 Provider 必须保留记录重试，禁止误判为对象缺失。
                    probeFailures++;
                    continue;
                }

                if (!exists
                    && string.Equals(
                        record.StorageState,
                        "publishing",
                        StringComparison.Ordinal))
                {
                    // publishing 表示上传方已取得发布所有权；对象尚不可见时无法区分慢写入与崩溃，必须保留人工恢复。
                    retainedPublishing++;
                    continue;
                }

                var affectedRows = await commandExecutor.ExecuteAsync(
                        exists ? HostFileSql.ReconcileReady : HostFileSql.PurgePending,
                        new
                        {
                            FileId = record.Id,
                            record.ProviderKey,
                            record.StorageKey,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (affectedRows is < 0 or > 1)
                {
                    throw new InvalidOperationException(
                        "Files pending reconciliation affected rows outside the expected boundary.");
                }

                if (affectedRows == 0)
                {
                    concurrentlyCompleted++;
                }
                else if (exists)
                {
                    promoted++;
                }
                else
                {
                    purged++;
                }
            }

            var last = records[^1];
            hasCursor = true;
            afterCreatedAtUtc = last.CreatedAtUtc;
            afterId = last.Id;
            if (records.Count < options.BatchSize)
            {
                break;
            }
        }

        return new PendingHostFileReconciliationResult(
            scanned,
            promoted,
            purged,
            probeFailures,
            retainedPublishing,
            concurrentlyCompleted,
            batches);
    }

    private SqlStatement SelectStatement() =>
        _provider switch
        {
            DatabaseProvider.SqlServer => HostFileSql.SelectPendingHostFilesSqlServer,
            DatabaseProvider.MySql => HostFileSql.SelectPendingHostFilesMySql,
            _ => throw new NotSupportedException(
                $"Unsupported database provider '{_provider}'."),
        };
}
