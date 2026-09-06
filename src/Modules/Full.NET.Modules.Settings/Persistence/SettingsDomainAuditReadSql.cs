using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Settings.Persistence;

/// <summary><c>fn_settings_domain_audit</c> 只读查询语句。</summary>
internal static class SettingsDomainAuditReadSql
{
    public static readonly SqlStatement ListByTraceId = new(
        "settings.domain_audit.list_by_trace_id",
        """
        SELECT Id,
               ActionKey,
               TraceId,
               DiffSummaryJson,
               OccurredAtUtc
        FROM fn_settings_domain_audit
        WHERE TraceId = @TraceId
        ORDER BY OccurredAtUtc DESC
        """,
        SqlDataScope.HostOnly);
}

/// <summary>域审计差异读取行投影。</summary>
internal sealed class SettingsDomainAuditDiffRecord
{
    public Guid Id { get; init; }

    public string ActionKey { get; init; } = string.Empty;

    public string? TraceId { get; init; }

    public string? DiffSummaryJson { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }
}
