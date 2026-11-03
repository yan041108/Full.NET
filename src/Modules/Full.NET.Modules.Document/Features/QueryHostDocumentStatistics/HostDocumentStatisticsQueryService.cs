using Full.NET.Modules.Document.Security;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.QueryHostDocumentStatistics;

/// <summary>
/// Host 文档统计只读查询服务。聚合活动文档数、按类型分组与分享计数三类指标，
/// 全部基于 SQL 端聚合；默认只覆盖调用者当前可读、未删除的 Published 文档，
/// 与回收站分离，防止软删除行被误计入活跃指标。
/// </summary>
internal sealed class HostDocumentStatisticsQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions, IDocumentAccessPolicy accessPolicy, IClock clock)
{
    public async Task<Result<HostDocumentStatisticsResponse>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var parameters = DocumentSqlParameters.Create(("ActorUserId", accessPolicy.ActorUserId),
            ("Now", now), ("DayStart", dayStart), ("DayEnd", dayStart.AddDays(1)));
        var summaryStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentItemSql.StatisticsSummarySqlServer,
            DatabaseProvider.MySql => DocumentItemSql.StatisticsSummaryMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        var byTypeStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentItemSql.StatisticsByTypeSqlServer,
            DatabaseProvider.MySql => DocumentItemSql.StatisticsByTypeMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        var shareCountStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentItemSql.StatisticsShareCountSqlServer,
            DatabaseProvider.MySql => DocumentItemSql.StatisticsShareCountMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        var summary = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentStatisticsSummaryRecord>(
                summaryStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false) ?? new DocumentStatisticsSummaryRecord();

        var byType = await queryExecutor
            .QueryAsync<DocumentStatisticsByTypeRecord>(
                byTypeStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);

        var byCategory = await queryExecutor
            .QueryAsync<DocumentStatisticsByCategoryRecord>(
                DocumentItemSql.StatisticsByCategory,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);

        var shareCounts = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentStatisticsShareCountRecord>(
                shareCountStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false) ?? new DocumentStatisticsShareCountRecord();

        var summaryResponse = new HostDocumentStatisticsSummaryResponse(
            summary.TotalItems,
            summary.TotalVersions,
            summary.TotalSizeBytes / 1024,
            FormatTotalSizeInfo(summary.TotalSizeBytes))
            { TotalSizeBytes = summary.TotalSizeBytes, HistorySizeBytes = summary.HistorySizeBytes };

        var byTypeResponse = byType
            .Select(r => new HostDocumentStatisticsTypeItem(r.Extension, r.Count, r.TotalSizeBytes / 1024) { TotalSizeBytes = r.TotalSizeBytes })
            .ToArray();

        var byCategoryResponse = byCategory
            .Select(r => new HostDocumentStatisticsCategoryItem(r.CategoryId, r.CategoryName, r.Count))
            .ToArray();

        var response = new HostDocumentStatisticsResponse(
            summaryResponse,
            byTypeResponse,
            byCategoryResponse,
            shareCounts.ShareCount,
            shareCounts.TodayAccessCount,
            shareCounts.TodayDownloadCount,
            shareCounts.TodayCreatedCount,
            shareCounts.RecycleBinCount)
            { TodayPreviewCount = shareCounts.TodayPreviewCount, TodayShareAccessCount = shareCounts.TodayShareAccessCount };

        return Result<HostDocumentStatisticsResponse>.Success(response);
    }

    private static string FormatTotalSizeInfo(long bytes) =>
        bytes < 1024 ? $"{bytes} B" : FormattableString.Invariant($"{bytes / 1024d:0.##} KB");
}
