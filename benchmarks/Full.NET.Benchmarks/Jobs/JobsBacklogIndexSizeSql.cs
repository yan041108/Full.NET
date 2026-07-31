namespace Full.NET.Benchmarks.Jobs;

public static class JobsBacklogIndexSizeSql
{
    public const string MySqlStatisticsRefreshSql =
        "SET SESSION information_schema_stats_expiry = 0";

    public const string MySqlAnalyzeTableSql =
        "ANALYZE TABLE fn_jobs_execution";

    public static string ForProvider(string provider) =>
        provider switch
        {
            "sqlserver" =>
                """
                SELECT COALESCE(SUM(ps.reserved_page_count), 0) * 8192
                FROM sys.dm_db_partition_stats ps
                INNER JOIN sys.indexes i
                    ON i.object_id = ps.object_id
                   AND i.index_id = ps.index_id
                WHERE ps.object_id =
                          OBJECT_ID(N'dbo.fn_jobs_execution')
                  AND i.name = @IndexName
                """,
            "mysql" =>
                """
                SELECT COALESCE(INDEX_LENGTH, 0)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'fn_jobs_execution'
                """,
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "不支持的 Jobs backlog 索引体积 Provider。"),
        };
}
