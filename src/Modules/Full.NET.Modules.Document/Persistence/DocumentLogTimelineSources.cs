using Full.NET.Abstractions.Observability;
using Full.NET.Data.Abstractions;
using Full.NET.Modularity.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>仅贡献本模块拥有的日志摘要；Host 查询不扩大为跨租户业务数据读取。</summary>
internal static class DocumentLogTimelineSources
{
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<ILogTimelineReader>(provider => new SqlLogTimelineReader(
            provider.GetRequiredService<IQueryExecutor>(), provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            "document.access.log", "document.host_access_logs.read", AccessLogSqlServer, AccessLogMySql));
        services.AddScoped<ILogTimelineReader>(provider => new SqlLogTimelineReader(
            provider.GetRequiredService<IQueryExecutor>(), provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            "document.version.deletion.audit", "document.host_documents.read", VersionDeletionAuditSqlServer, VersionDeletionAuditMySql));
    }

    private static readonly SqlStatement AccessLogSqlServer = new(
        "document.timeline_access_log.sqlserver",
        """
        SELECT TOP (@FetchSize) Id, OccurredAtUtc AS OccurredAtUtc, AccessTypeKey AS Summary,
               NULL AS TraceId, ActorUserId AS UserId, NULL AS TenantId,
               ClientIpFingerprint AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_document_access_log
        WHERE OccurredAtUtc >= @FromUtc AND OccurredAtUtc < @ToUtc
          AND (@TraceId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.TraceId') = @TraceId))
          AND (@SessionId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.SessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.CenterSessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ApplicationSessionId') = @SessionId))
          AND (@UserId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') = @UserId OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') IS NULL AND ActorUserId = @UserGuid)))
          AND (@ClientIpFingerprint IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ClientIpFingerprint') = @ClientIpFingerprint OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ClientIpFingerprint') IS NULL AND ClientIpFingerprint = @ClientIpFingerprint)))
        ORDER BY OccurredAtUtc, Id
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement AccessLogMySql = new(
        "document.timeline_access_log.mysql",
        """
        SELECT Id, OccurredAtUtc AS OccurredAtUtc, AccessTypeKey AS Summary,
               NULL AS TraceId, ActorUserId AS UserId, NULL AS TenantId,
               ClientIpFingerprint AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_document_access_log
        WHERE OccurredAtUtc >= @FromUtc AND OccurredAtUtc < @ToUtc
          AND (@TraceId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.TraceId')) = @TraceId))
          AND (@SessionId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.SessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.CenterSessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ApplicationSessionId')) = @SessionId))
          AND (@UserId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) = @UserId OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) IS NULL AND ActorUserId = @UserGuid)))
          AND (@ClientIpFingerprint IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ClientIpFingerprint')) = @ClientIpFingerprint OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ClientIpFingerprint')) IS NULL AND ClientIpFingerprint = @ClientIpFingerprint)))
        ORDER BY OccurredAtUtc, Id
        LIMIT @FetchSize
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement VersionDeletionAuditSqlServer = new(
        "document.timeline_version_deletion_audit.sqlserver",
        """
        SELECT TOP (@FetchSize) Id, DeletedAtUtc AS OccurredAtUtc, 'document.version.deleted' AS Summary,
               NULL AS TraceId, DeletedByUserId AS UserId, NULL AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_document_version_deletion_audit
        WHERE DeletedAtUtc >= @FromUtc AND DeletedAtUtc < @ToUtc
          AND (@TraceId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.TraceId') = @TraceId))
          AND (@SessionId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.SessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.CenterSessionId') = @SessionId OR JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ApplicationSessionId') = @SessionId))
          AND (@UserId IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') = @UserId OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.UserId') IS NULL AND DeletedByUserId = @UserGuid)))
          AND (@ClientIpFingerprint IS NULL OR (JSON_VALUE(CASE WHEN ISJSON(CorrelationJson)=1 THEN CorrelationJson ELSE '{}' END, '$.ClientIpFingerprint') = @ClientIpFingerprint))
        ORDER BY DeletedAtUtc, Id
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement VersionDeletionAuditMySql = new(
        "document.timeline_version_deletion_audit.mysql",
        """
        SELECT Id, DeletedAtUtc AS OccurredAtUtc, 'document.version.deleted' AS Summary,
               NULL AS TraceId, DeletedByUserId AS UserId, NULL AS TenantId,
               NULL AS ClientIpFingerprint, CorrelationJson, NULL AS SessionId, NULL AS CenterSessionId, NULL AS ApplicationSessionId
        FROM fn_document_version_deletion_audit
        WHERE DeletedAtUtc >= @FromUtc AND DeletedAtUtc < @ToUtc
          AND (@TraceId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.TraceId')) = @TraceId))
          AND (@SessionId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.SessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.CenterSessionId')) = @SessionId OR JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ApplicationSessionId')) = @SessionId))
          AND (@UserId IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) = @UserId OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.UserId')) IS NULL AND DeletedByUserId = @UserGuid)))
          AND (@ClientIpFingerprint IS NULL OR (JSON_UNQUOTE(JSON_EXTRACT(IF(JSON_VALID(CorrelationJson), CorrelationJson, '{}'), '$.ClientIpFingerprint')) = @ClientIpFingerprint))
        ORDER BY DeletedAtUtc, Id
        LIMIT @FetchSize
        """, SqlDataScope.HostOnly);
}
