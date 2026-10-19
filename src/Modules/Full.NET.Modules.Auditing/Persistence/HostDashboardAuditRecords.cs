/// <summary>Auditing 持久化相关类型；仅供 Dapper 或内部编排使用。</summary>
namespace Full.NET.Modules.Auditing.Persistence;

internal sealed class HostDashboardAccessMetricsRecord
{
    public long TodayRequestCount { get; set; }

    public decimal TodayErrorRate { get; set; }
}

internal sealed class HostDashboardActivityRecord
{
    public string ActionKey { get; set; } = string.Empty;

    public string HttpMethod { get; set; } = string.Empty;

    public string RequestPath { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}
