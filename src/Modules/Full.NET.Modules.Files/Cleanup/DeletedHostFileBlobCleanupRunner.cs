using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Cleanup;

/// <summary>单次 Blob 回收批次计数结果，包含扫描、清理、Blob 失败与并发完成统计。</summary>
internal sealed record DeletedHostFileBlobCleanupResult(
    int Scanned,
    int Purged,
    int BlobFailures,
    int ConcurrentlyCompleted,
    int BatchesExecuted)
{
    public static DeletedHostFileBlobCleanupResult Empty { get; } =
        new(0, 0, 0, 0, 0);
}

/// <summary>后台清理已软删除且无未释放 Claim 的 Host 文件 Blob 与元数据行。</summary>
/// <remarks>
/// 安全边界：清理只针对 <c>DeletedAtUtc IS NOT NULL</c> 的行，软删除前置守卫已在 <see cref="Persistence.HostFileSql.SoftDelete"/> 拒绝存在未释放 Claim 的文件；
/// Blob 删除失败不阻断元数据清理循环，失败计入 <see cref="DeletedHostFileBlobCleanupResult.BlobFailures"/> 供运维追踪；
/// 使用跨作用域共享游标和周期固定上界；周期结束后重试失败墓碑，可由配置 <c>Enabled</c> 关闭。
/// </remarks>
internal sealed class DeletedHostFileBlobCleanupRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    FileStorageProviderRegistry storageProviders,
    IOptions<DatabaseOptions> databaseOptions,
    IClock? clock = null,
    DeletedHostFileBlobCleanupCursor? cursor = null)
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;
    private readonly IClock _clock = clock ?? new SystemClock();
    private readonly DeletedHostFileBlobCleanupCursor _cursor = cursor ?? new();

    public async Task<DeletedHostFileBlobCleanupResult> RunOnceAsync(
        DeletedHostFileBlobCleanupOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return DeletedHostFileBlobCleanupResult.Empty;
        }

        var failures = DeletedHostFileBlobCleanupOptionsValidator.ValidateBounds(options);
        if (failures.Count != 0)
            throw new OptionsValidationException(DeletedHostFileBlobCleanupOptions.SectionName, options.GetType(), failures);
        await _cursor.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RunCoreAsync(options, cancellationToken).ConfigureAwait(false); }
        finally { _cursor.Gate.Release(); }
    }

    private async Task<DeletedHostFileBlobCleanupResult> RunCoreAsync(
        DeletedHostFileBlobCleanupOptions options, CancellationToken cancellationToken)
    {
        // 固定本周期上界，持续新增墓碑不能让失败记录无限等待下一轮重试。
        _cursor.ScanThroughUtc ??= _clock.UtcNow;

        var scanned = 0;
        var purged = 0;
        var blobFailures = 0;
        var concurrentlyCompleted = 0;
        var batches = 0;

        while (batches < options.MaxBatchesPerRun)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var records = await queryExecutor.QueryAsync<DeletedHostFileBlobRecord>(
                    SelectStatement(),
                    new Dictionary<string, object?>
                    {
                        ["HasCursor"] = _cursor.Id.HasValue ? 1 : 0,
                        ["AfterDeletedAtUtc"] = _cursor.DeletedAtUtc,
                        ["AfterId"] = _cursor.Id,
                        ["ScanThroughDeletedAtUtc"] = _cursor.ScanThroughUtc,
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
                try
                {
                    // 墓碑记录决定物理 Provider；未知机器码必须保留墓碑重试，禁止误删默认 Provider 对象。
                    var storageProvider = storageProviders.Resolve(record.ProviderKey);
                    await storageProvider.DeleteAsync(
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
                    // 单个不可删除对象保留墓碑并留到下轮重试，不能阻塞本轮后续候选。
                    blobFailures++;
                    continue;
                }

                var affectedRows = await commandExecutor.ExecuteAsync(
                        HostFileSql.PurgeDeletedHostFile,
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
                        "Files cleanup affected rows outside the expected tombstone boundary.");
                }

                if (affectedRows == 1)
                {
                    purged++;
                }
                else
                {
                    concurrentlyCompleted++;
                }
            }

            var last = records[^1];
            cancellationToken.ThrowIfCancellationRequested();
            // 只有整批执行完才推进；数据库异常或取消保留位置，已删除对象可幂等重试。
            _cursor.DeletedAtUtc = last.DeletedAtUtc;
            _cursor.Id = last.Id;
            if (records.Count < options.BatchSize)
            {
                _cursor.Reset();
                break;
            }
        }

        return new DeletedHostFileBlobCleanupResult(
            scanned,
            purged,
            blobFailures,
            concurrentlyCompleted,
            batches);
    }

    private SqlStatement SelectStatement() =>
        _provider switch
        {
            DatabaseProvider.SqlServer =>
                HostFileSql.SelectDeletedHostFilesSqlServer,
            DatabaseProvider.MySql =>
                HostFileSql.SelectDeletedHostFilesMySql,
            _ => throw new NotSupportedException(
                $"Unsupported database provider '{_provider}'."),
        };
}
