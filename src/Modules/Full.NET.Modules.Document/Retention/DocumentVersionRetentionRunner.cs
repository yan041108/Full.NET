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

        var candidates = await queryExecutor
            .QueryAsync<DocumentVersionRetentionCandidateRecord>(
                _provider == DatabaseProvider.MySql
                    ? DocumentVersionRetentionSql.ListItemsWithExcessHistoryMySql
                    : DocumentVersionRetentionSql.ListItemsWithExcessHistorySqlServer,
                DocumentSqlParameters.Create(
                    ("BatchSize", options.BatchSize),
                    ("MaximumRetainedHistoryVersions", options.MaximumRetainedHistoryVersions)),
                cancellationToken)
            .ConfigureAwait(false);

        var versionsDeleted = 0;
        var itemsProcessed = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            itemsProcessed++;

            var excessCount = candidate.HistoryCount - options.MaximumRetainedHistoryVersions;
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
                        ("TakeCount", excessCount)),
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var version in versions)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
