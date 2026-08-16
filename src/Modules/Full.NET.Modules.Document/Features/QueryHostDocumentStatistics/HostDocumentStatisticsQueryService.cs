using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.QueryHostDocumentStatistics;

internal sealed class HostDocumentStatisticsQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<HostDocumentStatisticsResponse>> GetAsync(
        CancellationToken cancellationToken = default)
    {
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
                null,
                cancellationToken)
            .ConfigureAwait(false) ?? new DocumentStatisticsSummaryRecord();

        var byType = await queryExecutor
            .QueryAsync<DocumentStatisticsByTypeRecord>(
                byTypeStatement,
                null,
                cancellationToken)
            .ConfigureAwait(false);

        var byCategory = await queryExecutor
            .QueryAsync<DocumentStatisticsByCategoryRecord>(
                DocumentItemSql.StatisticsByCategory,
                null,
                cancellationToken)
            .ConfigureAwait(false);

        var shareCounts = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentStatisticsShareCountRecord>(
                shareCountStatement,
                null,
                cancellationToken)
            .ConfigureAwait(false) ?? new DocumentStatisticsShareCountRecord();

        var summaryResponse = new HostDocumentStatisticsSummaryResponse(
            summary.TotalItems,
            summary.TotalVersions,
            summary.TotalSizeKb,
            FormatTotalSizeInfo(summary.TotalSizeKb));

        var byTypeResponse = byType
            .Select(r => new HostDocumentStatisticsTypeItem(r.Extension, r.Count, r.TotalSizeKb))
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
            shareCounts.TodayAccessCount,
            shareCounts.TodayCreatedCount,
            shareCounts.RecycleBinCount);

        return Result<HostDocumentStatisticsResponse>.Success(response);
    }

    private static string FormatTotalSizeInfo(long totalSizeKb)
    {
        const long kbPerMb = 1024;
        const long kbPerGb = 1024 * 1024;

        if (totalSizeKb <= 0)
        {
            return "0 B";
        }

        if (totalSizeKb < kbPerMb)
        {
            var bytes = totalSizeKb * 1024;
            return $"{bytes} B / {totalSizeKb} KB";
        }

        if (totalSizeKb < kbPerGb)
        {
            var mb = Math.Round((double)totalSizeKb / kbPerMb, 2);
            return $"{totalSizeKb} KB / {mb} MB";
        }
        else
        {
            var gb = Math.Round((double)totalSizeKb / kbPerGb, 2);
            var mb = Math.Round((double)totalSizeKb / kbPerMb, 2);
            return $"{mb} MB / {gb} GB";
        }
    }
}
