namespace Full.NET.Modules.Identity.Persistence;

internal sealed class HostDashboardActivityRecord
{
    public string ActionKey { get; set; } = string.Empty;

    public string HttpMethod { get; set; } = string.Empty;

    public string RequestPath { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}
