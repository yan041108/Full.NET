using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Retention;

internal sealed record DocumentVersionRetentionResult(int VersionsDeleted, int ItemsProcessed)
{
    public static DocumentVersionRetentionResult Empty { get; } = new(0, 0);
}

/// <summary>
/// 按 <see cref="DocumentVersionRetentionOptions.MaximumRetainedHistoryVersions"/> 裁剪超额历史版本；
/// 每个文档项始终保留当前版本与配置的最小版本总数。
/// </summary>
internal sealed class DocumentVersionRetentionRunner(
    IQueryExecutor queryExecutor,
    DocumentVersionDeletionService deletionService,
    IOptions<DatabaseOptions> databaseOptions)
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;

    public async Task<DocumentVersionRetentionResult> RunOnceAsync(
        DocumentVersionRetentionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaximumRetainedHistoryVersions <= 0)
        {
            return DocumentVersionRetentionResult.Empty;
        }

        // 最大历史数不得低于最小总保留量，否则不可裁剪的头项会反复耗尽批次预算。
        var retainedHistory = Math.Max(options.MaximumRetainedHistoryVersions, options.MinimumRetainedVersionsPerItem - 1);
        var candidates = await queryExecutor
            .QueryAsync<DocumentVersionRetentionCandidateRecord>(
                _provider == DatabaseProvider.MySql
                    ? DocumentVersionRetentionSql.ListItemsWithExcessHistoryMySql
                    : DocumentVersionRetentionSql.ListItemsWithExcessHistorySqlServer,
                DocumentSqlParameters.Create(
                    ("BatchSize", options.BatchSize),
                    ("MaximumRetainedHistoryVersions", retainedHistory)),
                cancellationToken)
            .ConfigureAwait(false);

        var remaining = Math.Clamp(options.BatchSize, 1, 1000);
        var versionsDeleted = 0;
        var itemsProcessed = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (remaining == 0) break;
            itemsProcessed++;

            var excessCount = candidate.HistoryCount - retainedHistory;
            if (excessCount <= 0)
            {
                continue;
            }

            var versions = await queryExecutor
                .QueryAsync<DocumentVersionRecord>(
                    _provider == DatabaseProvider.MySql
                        ? DocumentVersionRetentionSql.ListOldestHistoryVersionsMySql
                        : DocumentVersionRetentionSql.ListOldestHistoryVersionsSqlServer,
                    DocumentSqlParameters.Create(
                        ("DocumentItemId", candidate.DocumentItemId),
                        ("CurrentVersionId", candidate.CurrentVersionId),
                        ("TakeCount", Math.Min(excessCount, remaining))),
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var version in versions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (remaining == 0) break;
                // 失败尝试同样消耗预算，避免并发删除/最小保留冲突使本轮工作量失去上界。
                remaining--;
                if (await deletionService
                        .TryDeleteVersionForRetentionAsync(
                            candidate.DocumentItemId,
                            version.Id,
                            cancellationToken)
                        .ConfigureAwait(false))
                {
                    versionsDeleted++;
                }
            }
        }

        return new DocumentVersionRetentionResult(versionsDeleted, itemsProcessed);
    }
}
