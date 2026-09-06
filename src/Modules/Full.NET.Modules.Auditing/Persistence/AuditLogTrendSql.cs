using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Auditing.Persistence;

/// <summary>审计日志趋势聚合 SQL；按时间桶统计事件量与错误量。</summary>
internal static class AuditLogTrendSql
{
    public static readonly SqlStatement AccessTrendSqlServer = new(
        "auditing.trend.access.sqlserver",
        """
        SELECT DATEADD(
                   minute,
                   (DATEDIFF(minute, @FromUtc, OccurredAtUtc) / @BucketMinutes) * @BucketMinutes,
                   @FromUtc) AS BucketStartUtc,
               COUNT(1) AS EventCount,
               SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) AS ErrorCount
        FROM fn_auditing_access_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
        GROUP BY DATEADD(
                     minute,
                     (DATEDIFF(minute, @FromUtc, OccurredAtUtc) / @BucketMinutes) * @BucketMinutes,
                     @FromUtc)
        ORDER BY BucketStartUtc
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement AccessTrendMySql = new(
        "auditing.trend.access.mysql",
        """
        SELECT DATE_ADD(
                   @FromUtc,
                   INTERVAL FLOOR(TIMESTAMPDIFF(MINUTE, @FromUtc, OccurredAtUtc) / @BucketMinutes) * @BucketMinutes MINUTE
               ) AS BucketStartUtc,
               COUNT(1) AS EventCount,
               SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) AS ErrorCount
        FROM fn_auditing_access_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
        GROUP BY DATE_ADD(
                     @FromUtc,
                     INTERVAL FLOOR(TIMESTAMPDIFF(MINUTE, @FromUtc, OccurredAtUtc) / @BucketMinutes) * @BucketMinutes MINUTE
                 )
        ORDER BY BucketStartUtc
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement OperationTrendSqlServer = new(
        "auditing.trend.operation.sqlserver",
        """
        SELECT DATEADD(
                   minute,
                   (DATEDIFF(minute, @FromUtc, OccurredAtUtc) / @BucketMinutes) * @BucketMinutes,
                   @FromUtc) AS BucketStartUtc,
               COUNT(1) AS EventCount,
               SUM(CASE WHEN Succeeded = 0 THEN 1 ELSE 0 END) AS ErrorCount
        FROM fn_auditing_operation_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
        GROUP BY DATEADD(
                     minute,
                     (DATEDIFF(minute, @FromUtc, OccurredAtUtc) / @BucketMinutes) * @BucketMinutes,
                     @FromUtc)
        ORDER BY BucketStartUtc
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement OperationTrendMySql = new(
        "auditing.trend.operation.mysql",
        """
        SELECT DATE_ADD(
                   @FromUtc,
                   INTERVAL FLOOR(TIMESTAMPDIFF(MINUTE, @FromUtc, OccurredAtUtc) / @BucketMinutes) * @BucketMinutes MINUTE
               ) AS BucketStartUtc,
               COUNT(1) AS EventCount,
               SUM(CASE WHEN Succeeded = 0 THEN 1 ELSE 0 END) AS ErrorCount
        FROM fn_auditing_operation_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
        GROUP BY DATE_ADD(
                     @FromUtc,
                     INTERVAL FLOOR(TIMESTAMPDIFF(MINUTE, @FromUtc, OccurredAtUtc) / @BucketMinutes) * @BucketMinutes MINUTE
                 )
        ORDER BY BucketStartUtc
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ExceptionTrendSqlServer = new(
        "auditing.trend.exception.sqlserver",
        """
        SELECT DATEADD(
                   minute,
                   (DATEDIFF(minute, @FromUtc, OccurredAtUtc) / @BucketMinutes) * @BucketMinutes,
                   @FromUtc) AS BucketStartUtc,
               COUNT(1) AS EventCount,
               COUNT(1) AS ErrorCount
        FROM fn_auditing_exception_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
        GROUP BY DATEADD(
                     minute,
                     (DATEDIFF(minute, @FromUtc, OccurredAtUtc) / @BucketMinutes) * @BucketMinutes,
                     @FromUtc)
        ORDER BY BucketStartUtc
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ExceptionTrendMySql = new(
        "auditing.trend.exception.mysql",
        """
        SELECT DATE_ADD(
                   @FromUtc,
                   INTERVAL FLOOR(TIMESTAMPDIFF(MINUTE, @FromUtc, OccurredAtUtc) / @BucketMinutes) * @BucketMinutes MINUTE
               ) AS BucketStartUtc,
               COUNT(1) AS EventCount,
               COUNT(1) AS ErrorCount
        FROM fn_auditing_exception_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
        GROUP BY DATE_ADD(
                     @FromUtc,
                     INTERVAL FLOOR(TIMESTAMPDIFF(MINUTE, @FromUtc, OccurredAtUtc) / @BucketMinutes) * @BucketMinutes MINUTE
                 )
        ORDER BY BucketStartUtc
        """,
        SqlDataScope.HostOnly);
}

/// <summary>趋势桶查询行投影。</summary>
internal sealed class AuditLogTrendBucketRecord
{
    public DateTimeOffset BucketStartUtc { get; init; }

    public long EventCount { get; init; }

    public long ErrorCount { get; init; }
}
