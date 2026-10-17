using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
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
    IEnumerable<IHostFileRetentionContributor> retentionContributors,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock,
    PendingHostFileReconciliationCursor? cursor = null)
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;
    private readonly PendingHostFileReconciliationCursor _cursor = cursor ?? new();

    public async Task<PendingHostFileReconciliationResult> RunOnceAsync(
        PendingHostFileReconciliationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return PendingHostFileReconciliationResult.Empty;
        }

        var validation = new PendingHostFileReconciliationOptionsValidator().Validate(null, options);
        if (validation.Failed)
            throw new OptionsValidationException(PendingHostFileReconciliationOptions.SectionName, options.GetType(), validation.Failures);
        await _cursor.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RunCoreAsync(options, cancellationToken).ConfigureAwait(false); }
        finally { _cursor.Gate.Release(); }
    }

    private async Task<PendingHostFileReconciliationResult> RunCoreAsync(
        PendingHostFileReconciliationOptions options, CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.AddSeconds(-options.MinimumAgeSeconds);
        // 周期上界保持固定；保护窗口收紧时只向更早上界收敛。
        _cursor.CreatedBeforeUtc = _cursor.CreatedBeforeUtc is { } previous && previous < cutoff ? previous : cutoff;
        var scanned = 0;
        var promoted = 0;
        var purged = 0;
        var probeFailures = 0;
        var retainedPublishing = 0;
        var concurrentlyCompleted = 0;
        var batches = 0;

        while (batches < options.MaxBatchesPerRun)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var records = await queryExecutor.QueryAsync<PendingHostFileRecord>(
                    SelectStatement(),
                    new Dictionary<string, object?>
                    {
                        ["CreatedBeforeUtc"] = _cursor.CreatedBeforeUtc,
                        ["HasCursor"] = _cursor.Id.HasValue ? 1 : 0,
                        ["AfterCreatedAtUtc"] = _cursor.CreatedAtUtc,
                        ["AfterId"] = _cursor.Id,
                        ["BatchSize"] = options.BatchSize,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            batches++;
            if (records.Count == 0)
            {
                _cursor.Reset();
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
                    cancellationToken.ThrowIfCancellationRequested();
                    // 未知或不可用的 Provider 必须保留记录重试，禁止误判为对象缺失。
                    probeFailures++;
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

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

                if (!exists && await IsFileRetainedByContributorAsync(record.Id, cancellationToken)
                        .ConfigureAwait(false))
                {
                    retainedPublishing++;
                    continue;
                }

                var affectedRows = await commandExecutor.ExecuteAsync(
                        exists ? HostFileSql.ReconcileReady : HostFileSql.PurgePending,
                        new Dictionary<string, object?>
                        {
                            ["FileId"] = record.Id,
                            ["ProviderKey"] = record.ProviderKey,
                            ["StorageKey"] = record.StorageKey,
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
            cancellationToken.ThrowIfCancellationRequested();
            _cursor.CreatedAtUtc = last.CreatedAtUtc;
            _cursor.Id = last.Id;
            if (records.Count < options.BatchSize)
            {
                _cursor.Reset();
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

    private async Task<bool> IsFileRetainedByContributorAsync(
        Guid fileId,
        CancellationToken cancellationToken)
    {
        foreach (var contributor in retentionContributors)
        {
            var retained = await contributor.IsFileReferencedAsync(fileId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (retained)
            {
                return true;
            }
        }

        return false;
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
