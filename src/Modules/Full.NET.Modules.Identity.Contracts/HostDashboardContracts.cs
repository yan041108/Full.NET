namespace Full.NET.Modules.Identity.Contracts;

public sealed record HostDashboardActivityResponse(
    string ActionKey,
    string HttpMethod,
    string RequestPath,
    bool Succeeded,
    DateTimeOffset OccurredAtUtc);

public sealed record HostDashboardSummaryResponse(
    long ActiveTenantCount,
    long OnlineSessionCount,
    long TodayRequestCount,
    decimal TodayErrorRate,
    HostDashboardActivityResponse[] RecentActivities);
