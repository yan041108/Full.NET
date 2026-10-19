using Full.NET.Abstractions.Observability;
using Full.NET.Data.Abstractions;
using Full.NET.Modularity.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>仅贡献本模块拥有的日志摘要；Host 查询不扩大为跨租户业务数据读取。</summary>
internal static class IdentityLogTimelineSources
{
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<ILogTimelineReader>(provider => new SqlLogTimelineReader(
            provider.GetRequiredService<IQueryExecutor>(), provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            "identity.auth.audit", "identity.authentication_events.read", AuthAuditSqlServer, AuthAuditMySql));
    }

    private static readonly SqlStatement AuthAuditSqlServer = new(
        "identity.timeline_auth_audit.sqlserver",
        """
        SELECT TOP (@FetchSize) Id, OccurredAtUtc AS OccurredAtUtc, EventType AS Summary,
               TraceId AS TraceId, UserId AS UserId, ContextTenantId AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, SessionId, CenterSessionId, ApplicationSessionId
        FROM fn_identity_auth_audit
        WHERE OccurredAtUtc >= @FromUtc AND OccurredAtUtc < @ToUtc
          AND (@TraceId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.TraceId') = @TraceId OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.TraceId') IS NULL AND TraceId = @TraceId)))
          AND (@SessionId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.SessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.CenterSessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ApplicationSessionId') = @SessionId OR SessionId = @SessionGuid OR CenterSessionId = @SessionGuid OR ApplicationSessionId = @SessionGuid))
          AND (@UserId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') = @UserId OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') IS NULL AND UserId = @UserGuid)))
          AND (@ClientIpFingerprint IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ClientIpFingerprint') = @ClientIpFingerprint))
        ORDER BY OccurredAtUtc, Id
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement AuthAuditMySql = new(
        "identity.timeline_auth_audit.mysql",
        """
        SELECT Id, OccurredAtUtc AS OccurredAtUtc, EventType AS Summary,
               TraceId AS TraceId, UserId AS UserId, ContextTenantId AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, SessionId, CenterSessionId, ApplicationSessionId
        FROM fn_identity_auth_audit
        WHERE OccurredAtUtc >= @FromUtc AND OccurredAtUtc < @ToUtc
          AND (@TraceId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.TraceId')) = @TraceId OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.TraceId')) IS NULL AND TraceId = @TraceId)))
          AND (@SessionId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.SessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.CenterSessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ApplicationSessionId')) = @SessionId OR SessionId = @SessionGuid OR CenterSessionId = @SessionGuid OR ApplicationSessionId = @SessionGuid))
          AND (@UserId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) = @UserId OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) IS NULL AND UserId = @UserGuid)))
          AND (@ClientIpFingerprint IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ClientIpFingerprint')) = @ClientIpFingerprint))
        ORDER BY OccurredAtUtc, Id
        LIMIT @FetchSize
        """, SqlDataScope.HostOnly);
}
