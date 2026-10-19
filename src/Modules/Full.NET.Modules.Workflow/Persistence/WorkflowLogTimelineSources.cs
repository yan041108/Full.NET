using Full.NET.Abstractions.Observability;
using Full.NET.Data.Abstractions;
using Full.NET.Modularity.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>仅贡献本模块拥有的日志摘要；Host 查询不扩大为跨租户业务数据读取。</summary>
internal static class WorkflowLogTimelineSources
{
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<ILogTimelineReader>(provider => new SqlLogTimelineReader(
            provider.GetRequiredService<IQueryExecutor>(), provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            "workflow.domain.audit", "workflow.instances.read", DomainAuditSqlServer, DomainAuditMySql));
        services.AddScoped<ILogTimelineReader>(provider => new SqlLogTimelineReader(
            provider.GetRequiredService<IQueryExecutor>(), provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            "workflow.execution.log", "workflow.instances.read", ExecutionLogSqlServer, ExecutionLogMySql));
    }

    private static readonly SqlStatement DomainAuditSqlServer = new(
        "workflow.timeline_domain_audit.sqlserver",
        """
        SELECT TOP (@FetchSize) Id, CreatedAtUtc AS OccurredAtUtc, OperationKey AS Summary,
               NULL AS TraceId, ActorUserId AS UserId, TenantId AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_workflow_domain_audit
        WHERE CreatedAtUtc >= @FromUtc AND CreatedAtUtc < @ToUtc
          AND TenantId IS NULL
          AND (@TraceId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.TraceId') = @TraceId))
          AND (@SessionId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.SessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.CenterSessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ApplicationSessionId') = @SessionId))
          AND (@UserId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') = @UserId OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') IS NULL AND ActorUserId = @UserGuid)))
          AND (@ClientIpFingerprint IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ClientIpFingerprint') = @ClientIpFingerprint))
        ORDER BY CreatedAtUtc, Id
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement DomainAuditMySql = new(
        "workflow.timeline_domain_audit.mysql",
        """
        SELECT Id, CreatedAtUtc AS OccurredAtUtc, OperationKey AS Summary,
               NULL AS TraceId, ActorUserId AS UserId, TenantId AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_workflow_domain_audit
        WHERE CreatedAtUtc >= @FromUtc AND CreatedAtUtc < @ToUtc
          AND TenantId IS NULL
          AND (@TraceId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.TraceId')) = @TraceId))
          AND (@SessionId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.SessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.CenterSessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ApplicationSessionId')) = @SessionId))
          AND (@UserId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) = @UserId OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) IS NULL AND ActorUserId = @UserGuid)))
          AND (@ClientIpFingerprint IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ClientIpFingerprint')) = @ClientIpFingerprint))
        ORDER BY CreatedAtUtc, Id
        LIMIT @FetchSize
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement ExecutionLogSqlServer = new(
        "workflow.timeline_execution_log.sqlserver",
        """
        SELECT TOP (@FetchSize) Id, CreatedAtUtc AS OccurredAtUtc, TransitionKey AS Summary,
               NULL AS TraceId, NULL AS UserId, NULL AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_workflow_execution_log
        WHERE CreatedAtUtc >= @FromUtc AND CreatedAtUtc < @ToUtc
          AND EXISTS (SELECT 1 FROM fn_workflow_instance i WHERE i.Id=fn_workflow_execution_log.InstanceId AND i.TenantId IS NULL)
          AND (@TraceId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.TraceId') = @TraceId))
          AND (@SessionId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.SessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.CenterSessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ApplicationSessionId') = @SessionId))
          AND (@UserId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') = @UserId))
          AND (@ClientIpFingerprint IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ClientIpFingerprint') = @ClientIpFingerprint))
        ORDER BY CreatedAtUtc, Id
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement ExecutionLogMySql = new(
        "workflow.timeline_execution_log.mysql",
        """
        SELECT Id, CreatedAtUtc AS OccurredAtUtc, TransitionKey AS Summary,
               NULL AS TraceId, NULL AS UserId, NULL AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_workflow_execution_log
        WHERE CreatedAtUtc >= @FromUtc AND CreatedAtUtc < @ToUtc
          AND EXISTS (SELECT 1 FROM fn_workflow_instance i WHERE i.Id=fn_workflow_execution_log.InstanceId AND i.TenantId IS NULL)
          AND (@TraceId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.TraceId')) = @TraceId))
          AND (@SessionId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.SessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.CenterSessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ApplicationSessionId')) = @SessionId))
          AND (@UserId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) = @UserId))
          AND (@ClientIpFingerprint IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ClientIpFingerprint')) = @ClientIpFingerprint))
        ORDER BY CreatedAtUtc, Id
        LIMIT @FetchSize
        """, SqlDataScope.HostOnly);
}
