using Full.NET.Abstractions.Observability;
using Full.NET.Data.Abstractions;
using Full.NET.Modularity.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Messaging.Persistence;

/// <summary>仅贡献本模块拥有的日志摘要；Host 查询不扩大为跨租户业务数据读取。</summary>
internal static class MessagingLogTimelineSources
{
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<ILogTimelineReader>(provider => new SqlLogTimelineReader(
            provider.GetRequiredService<IQueryExecutor>(), provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            "messaging.domain.audit", "auditing.change_diff.read", DomainAuditSqlServer, DomainAuditMySql));
    }

    private static readonly SqlStatement DomainAuditSqlServer = new(
        "messaging.timeline_domain_audit.sqlserver",
        """
        SELECT TOP (@FetchSize) Id, OccurredAtUtc AS OccurredAtUtc, ActionKey AS Summary,
               TraceId AS TraceId, ActorUserId AS UserId, TenantId AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_messaging_domain_audit
        WHERE OccurredAtUtc >= @FromUtc AND OccurredAtUtc < @ToUtc
          AND TenantId IS NULL
          AND (@TraceId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.TraceId') = @TraceId OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.TraceId') IS NULL AND TraceId = @TraceId)))
          AND (@SessionId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.SessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.CenterSessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ApplicationSessionId') = @SessionId))
          AND (@UserId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') = @UserId OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') IS NULL AND ActorUserId = @UserGuid)))
          AND (@ClientIpFingerprint IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ClientIpFingerprint') = @ClientIpFingerprint))
        ORDER BY OccurredAtUtc, Id
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement DomainAuditMySql = new(
        "messaging.timeline_domain_audit.mysql",
        """
        SELECT Id, OccurredAtUtc AS OccurredAtUtc, ActionKey AS Summary,
               TraceId AS TraceId, ActorUserId AS UserId, TenantId AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_messaging_domain_audit
        WHERE OccurredAtUtc >= @FromUtc AND OccurredAtUtc < @ToUtc
          AND TenantId IS NULL
          AND (@TraceId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.TraceId')) = @TraceId OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.TraceId')) IS NULL AND TraceId = @TraceId)))
          AND (@SessionId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.SessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.CenterSessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ApplicationSessionId')) = @SessionId))
          AND (@UserId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) = @UserId OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) IS NULL AND ActorUserId = @UserGuid)))
          AND (@ClientIpFingerprint IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ClientIpFingerprint')) = @ClientIpFingerprint))
        ORDER BY OccurredAtUtc, Id
        LIMIT @FetchSize
        """, SqlDataScope.HostOnly);
}
