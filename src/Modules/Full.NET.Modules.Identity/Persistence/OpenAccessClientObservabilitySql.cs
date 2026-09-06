using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>OpenAccess 接入方可观测性 SQL；访问日志仅按 AccessKeyId 指纹隔离，禁止跨应用读取。</summary>
internal static class OpenAccessClientObservabilitySql
{
    private const string AccessLogEventTypesFilter = """
        audit.EventType IN ('signature_authentication', 'open_access_api_key_authentication')
        """;

    public static readonly SqlStatement FindClientAccessKeyById = new(
        "identity.find_open_access_client_access_key_by_id",
        """
        SELECT client.Id AS ClientId,
               apiKey.KeyPrefix AS AccessKeyId
        FROM fn_identity_open_access_client AS client
        INNER JOIN fn_identity_api_key AS apiKey ON apiKey.Id = client.ApiKeyId
        WHERE client.Id = @ClientId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindQuotaByApiKeyId = new(
        "identity.find_open_access_client_quota_by_api_key_id",
        """
        SELECT client.Id AS ClientId,
               client.DailyRequestQuota,
               apiKey.KeyPrefix AS AccessKeyId
        FROM fn_identity_open_access_client AS client
        INNER JOIN fn_identity_api_key AS apiKey ON apiKey.Id = client.ApiKeyId
        WHERE client.ApiKeyId = @ApiKeyId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountAccessLogsSqlServer = new(
        "identity.count_open_access_client_access_logs.sql_server",
        $"""
        SELECT COUNT(1)
        FROM fn_identity_auth_audit AS audit
        WHERE audit.UsernameFingerprint = @AccessKeyFingerprint
          AND {AccessLogEventTypesFilter}
          AND (@Succeeded IS NULL OR audit.Succeeded = @Succeeded)
          AND (@FromUtc IS NULL OR audit.OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR audit.OccurredAtUtc < @ToUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountAccessLogsMySql = new(
        "identity.count_open_access_client_access_logs.mysql",
        $"""
        SELECT COUNT(1)
        FROM fn_identity_auth_audit AS audit
        WHERE audit.UsernameFingerprint = @AccessKeyFingerprint
          AND {AccessLogEventTypesFilter}
          AND (@Succeeded IS NULL OR audit.Succeeded = @Succeeded)
          AND (@FromUtc IS NULL OR audit.OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR audit.OccurredAtUtc < @ToUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListAccessLogsSqlServer = new(
        "identity.list_open_access_client_access_logs.sql_server",
        $"""
        SELECT audit.Id,
               audit.EventType,
               audit.ResultCode,
               audit.Succeeded,
               audit.IpAddress,
               audit.UserAgent,
               audit.OccurredAtUtc
        FROM fn_identity_auth_audit AS audit
        WHERE audit.UsernameFingerprint = @AccessKeyFingerprint
          AND {AccessLogEventTypesFilter}
          AND (@Succeeded IS NULL OR audit.Succeeded = @Succeeded)
          AND (@FromUtc IS NULL OR audit.OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR audit.OccurredAtUtc < @ToUtc)
        ORDER BY audit.OccurredAtUtc DESC, audit.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListAccessLogsMySql = new(
        "identity.list_open_access_client_access_logs.mysql",
        $"""
        SELECT audit.Id,
               audit.EventType,
               audit.ResultCode,
               audit.Succeeded,
               audit.IpAddress,
               audit.UserAgent,
               audit.OccurredAtUtc
        FROM fn_identity_auth_audit AS audit
        WHERE audit.UsernameFingerprint = @AccessKeyFingerprint
          AND {AccessLogEventTypesFilter}
          AND (@Succeeded IS NULL OR audit.Succeeded = @Succeeded)
          AND (@FromUtc IS NULL OR audit.OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR audit.OccurredAtUtc < @ToUtc)
        ORDER BY audit.OccurredAtUtc DESC, audit.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountTodayUsageSqlServer = new(
        "identity.count_open_access_client_today_usage.sql_server",
        $"""
        SELECT
            SUM(CASE WHEN audit.Succeeded = 1 THEN 1 ELSE 0 END) AS SuccessCount,
            SUM(CASE WHEN audit.Succeeded = 0 THEN 1 ELSE 0 END) AS FailureCount
        FROM fn_identity_auth_audit AS audit
        WHERE audit.UsernameFingerprint = @AccessKeyFingerprint
          AND {AccessLogEventTypesFilter}
          AND audit.OccurredAtUtc >= @WindowStartUtc
          AND audit.OccurredAtUtc < @WindowEndUtc
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountTodayUsageMySql = new(
        "identity.count_open_access_client_today_usage.mysql",
        $"""
        SELECT
            SUM(CASE WHEN audit.Succeeded = 1 THEN 1 ELSE 0 END) AS SuccessCount,
            SUM(CASE WHEN audit.Succeeded = 0 THEN 1 ELSE 0 END) AS FailureCount
        FROM fn_identity_auth_audit AS audit
        WHERE audit.UsernameFingerprint = @AccessKeyFingerprint
          AND {AccessLogEventTypesFilter}
          AND audit.OccurredAtUtc >= @WindowStartUtc
          AND audit.OccurredAtUtc < @WindowEndUtc
        """,
        SqlDataScope.HostOnly);
}

/// <summary>接入方应用访问审计行。</summary>
internal sealed class OpenAccessClientAccessLogRow
{
    public Guid Id { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string ResultCode { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}

/// <summary>接入方应用 AccessKey 指纹解析行。</summary>
internal sealed class OpenAccessClientAccessKeyRow
{
    public Guid ClientId { get; set; }

    public string AccessKeyId { get; set; } = string.Empty;
}

/// <summary>接入方应用配额行。</summary>
internal sealed class OpenAccessClientQuotaRow
{
    public Guid ClientId { get; set; }

    public int? DailyRequestQuota { get; set; }

    public string AccessKeyId { get; set; } = string.Empty;
}

/// <summary>接入方应用当日用量统计行。</summary>
internal sealed class OpenAccessClientUsageCountRow
{
    public long SuccessCount { get; set; }

    public long FailureCount { get; set; }
}
