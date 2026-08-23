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
/// 使用 keyset 游标分页与受控批大小，避免长事务与无界扫描；可由配置 <c>Enabled</c> 关闭。
/// </remarks>
internal sealed class DeletedHostFileBlobCleanupRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    FileStorageProviderRegistry storageProviders,
    IOptions<DatabaseOptions> databaseOptions)
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;

    public async Task<DeletedHostFileBlobCleanupResult> RunOnceAsync(
        DeletedHostFileBlobCleanupOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return DeletedHostFileBlobCleanupResult.Empty;
        }

        var scanned = 0;
        var purged = 0;
        var blobFailures = 0;
        var concurrentlyCompleted = 0;
        var batches = 0;
        var hasCursor = false;
        var afterDeletedAtUtc = DateTimeOffset.UnixEpoch;
        Guid? afterId = null;

        while (batches < options.MaxBatchesPerRun)
        {
            var records = await queryExecutor.QueryAsync<DeletedHostFileBlobRecord>(
                    SelectStatement(),
                    new
                    {
                        HasCursor = hasCursor ? 1 : 0,
                        AfterDeletedAtUtc = afterDeletedAtUtc,
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
                    // 单个不可删除对象保留墓碑并留到下轮重试，不能阻塞本轮后续候选。
                    blobFailures++;
                    continue;
                }

                var affectedRows = await commandExecutor.ExecuteAsync(
                        HostFileSql.PurgeDeletedHostFile,
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
            hasCursor = true;
            afterDeletedAtUtc = last.DeletedAtUtc;
            afterId = last.Id;
            if (records.Count < options.BatchSize)
            {
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
