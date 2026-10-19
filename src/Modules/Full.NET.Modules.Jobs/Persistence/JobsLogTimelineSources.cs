using Full.NET.Abstractions.Observability;
using Full.NET.Data.Abstractions;
using Full.NET.Modularity.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Persistence;

/// <summary>仅贡献本模块拥有的日志摘要；Host 查询不扩大为跨租户业务数据读取。</summary>
internal static class JobsLogTimelineSources
{
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<ILogTimelineReader>(provider => new SqlLogTimelineReader(
            provider.GetRequiredService<IQueryExecutor>(), provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            "jobs.execution", "jobs.executions.read", ExecutionSqlServer, ExecutionMySql));
    }

    private static readonly SqlStatement ExecutionSqlServer = new(
        "jobs.timeline_execution.sqlserver",
        """
        SELECT TOP (@FetchSize) Id, CreatedAtUtc AS OccurredAtUtc, 'job.execution.created' AS Summary,
               NULL AS TraceId, NULL AS UserId, TenantId AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_jobs_execution
        WHERE CreatedAtUtc >= @FromUtc AND CreatedAtUtc < @ToUtc
          AND TenantId IS NULL
          AND (@TraceId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.TraceId') = @TraceId))
          AND (@SessionId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.SessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.CenterSessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ApplicationSessionId') = @SessionId))
          AND (@UserId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') = @UserId))
          AND (@ClientIpFingerprint IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ClientIpFingerprint') = @ClientIpFingerprint))
        ORDER BY CreatedAtUtc, Id
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement ExecutionMySql = new(
        "jobs.timeline_execution.mysql",
        """
        SELECT Id, CreatedAtUtc AS OccurredAtUtc, 'job.execution.created' AS Summary,
               NULL AS TraceId, NULL AS UserId, TenantId AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_jobs_execution
        WHERE CreatedAtUtc >= @FromUtc AND CreatedAtUtc < @ToUtc
          AND TenantId IS NULL
          AND (@TraceId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.TraceId')) = @TraceId))
          AND (@SessionId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.SessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.CenterSessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ApplicationSessionId')) = @SessionId))
          AND (@UserId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) = @UserId))
          AND (@ClientIpFingerprint IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ClientIpFingerprint')) = @ClientIpFingerprint))
        ORDER BY CreatedAtUtc, Id
        LIMIT @FetchSize
        """, SqlDataScope.HostOnly);
}
