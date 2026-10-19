using Full.NET.Abstractions.Observability;
using Full.NET.Data.Abstractions;
using Full.NET.Modularity.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>仅贡献本模块拥有的日志摘要；Host 查询不扩大为跨租户业务数据读取。</summary>
internal static class NotificationsLogTimelineSources
{
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<ILogTimelineReader>(provider => new SqlLogTimelineReader(
            provider.GetRequiredService<IQueryExecutor>(), provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            "notifications.domain.audit", "notifications.deliveries.read", DomainAuditSqlServer, DomainAuditMySql));
        services.AddScoped<ILogTimelineReader>(provider => new SqlLogTimelineReader(
            provider.GetRequiredService<IQueryExecutor>(), provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            "notifications.delivery.attempt", "notifications.deliveries.read", DeliveryAttemptSqlServer, DeliveryAttemptMySql));
    }

    private static readonly SqlStatement DomainAuditSqlServer = new(
        "notifications.timeline_domain_audit.sqlserver",
        """
        SELECT TOP (@FetchSize) Id, CreatedAtUtc AS OccurredAtUtc, OperationKey AS Summary,
               NULL AS TraceId, ActorUserId AS UserId, TenantId AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_notifications_domain_audit
        WHERE CreatedAtUtc >= @FromUtc AND CreatedAtUtc < @ToUtc
          AND TenantId IS NULL
          AND (@TraceId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.TraceId') = @TraceId))
          AND (@SessionId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.SessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.CenterSessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ApplicationSessionId') = @SessionId))
          AND (@UserId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') = @UserId OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') IS NULL AND ActorUserId = @UserGuid)))
          AND (@ClientIpFingerprint IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ClientIpFingerprint') = @ClientIpFingerprint))
        ORDER BY CreatedAtUtc, Id
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement DomainAuditMySql = new(
        "notifications.timeline_domain_audit.mysql",
        """
        SELECT Id, CreatedAtUtc AS OccurredAtUtc, OperationKey AS Summary,
               NULL AS TraceId, ActorUserId AS UserId, TenantId AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_notifications_domain_audit
        WHERE CreatedAtUtc >= @FromUtc AND CreatedAtUtc < @ToUtc
          AND TenantId IS NULL
          AND (@TraceId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.TraceId')) = @TraceId))
          AND (@SessionId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.SessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.CenterSessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ApplicationSessionId')) = @SessionId))
          AND (@UserId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) = @UserId OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) IS NULL AND ActorUserId = @UserGuid)))
          AND (@ClientIpFingerprint IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ClientIpFingerprint')) = @ClientIpFingerprint))
        ORDER BY CreatedAtUtc, Id
        LIMIT @FetchSize
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement DeliveryAttemptSqlServer = new(
        "notifications.timeline_delivery_attempt.sqlserver",
        """
        SELECT TOP (@FetchSize) Id, StartedAtUtc AS OccurredAtUtc, 'notification.attempt' AS Summary,
               NULL AS TraceId, NULL AS UserId, NULL AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_notifications_delivery_attempt
        WHERE StartedAtUtc >= @FromUtc AND StartedAtUtc < @ToUtc
          AND EXISTS (SELECT 1 FROM fn_notifications_delivery d
              INNER JOIN fn_notifications_intent i ON i.Id = d.IntentId
              WHERE d.Id = fn_notifications_delivery_attempt.DeliveryId AND i.TenantId IS NULL)
          AND (@TraceId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.TraceId') = @TraceId))
          AND (@SessionId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.SessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.CenterSessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ApplicationSessionId') = @SessionId))
          AND (@UserId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') = @UserId))
          AND (@ClientIpFingerprint IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ClientIpFingerprint') = @ClientIpFingerprint))
        ORDER BY StartedAtUtc, Id
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement DeliveryAttemptMySql = new(
        "notifications.timeline_delivery_attempt.mysql",
        """
        SELECT Id, StartedAtUtc AS OccurredAtUtc, 'notification.attempt' AS Summary,
               NULL AS TraceId, NULL AS UserId, NULL AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_notifications_delivery_attempt
        WHERE StartedAtUtc >= @FromUtc AND StartedAtUtc < @ToUtc
          AND EXISTS (SELECT 1 FROM fn_notifications_delivery d
              INNER JOIN fn_notifications_intent i ON i.Id = d.IntentId
              WHERE d.Id = fn_notifications_delivery_attempt.DeliveryId AND i.TenantId IS NULL)
          AND (@TraceId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.TraceId')) = @TraceId))
          AND (@SessionId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.SessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.CenterSessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ApplicationSessionId')) = @SessionId))
          AND (@UserId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) = @UserId))
          AND (@ClientIpFingerprint IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ClientIpFingerprint')) = @ClientIpFingerprint))
        ORDER BY StartedAtUtc, Id
        LIMIT @FetchSize
        """, SqlDataScope.HostOnly);
}
