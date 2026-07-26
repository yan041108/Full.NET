using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.GetHostDashboardSummary;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostDashboardQueryServiceTests
{
    [TestMethod]
    public async Task Summary_uses_owner_metrics_and_degrades_when_optional_readers_are_absent()
    {
        var now = new DateTimeOffset(2026, 7, 26, 9, 30, 0, TimeSpan.Zero);
        var queryExecutor = new OnlineSessionQueryExecutor(3);

        var reducedProfileService = new HostDashboardQueryService(
            queryExecutor,
            new FixedClock(now),
            [],
            []);
        var reducedProfile = await reducedProfileService.GetSummaryAsync();

        Assert.IsTrue(reducedProfile.IsSuccess);
        Assert.AreEqual(0, reducedProfile.Value!.ActiveTenantCount);
        Assert.AreEqual(3, reducedProfile.Value.OnlineSessionCount);
        Assert.AreEqual(0, reducedProfile.Value.TodayRequestCount);
        Assert.AreEqual(0m, reducedProfile.Value.TodayErrorRate);
        Assert.IsEmpty(reducedProfile.Value.RecentActivities);

        var tenantReader = new RecordingTenantMetricsReader(4);
        var expectedActivities =
            new[]
            {
                new HostDashboardActivityResponse(
                    "identity.roles.disable",
                    "POST",
                    "/api/v1/identity/roles/id/disable",
                    false,
                    now.AddMinutes(-1)),
            };
        var auditReader = new RecordingAuditMetricsReader(
            new HostDashboardAuditMetrics(12, 0.25m, expectedActivities));
        var fullProfileService = new HostDashboardQueryService(
            queryExecutor,
            new FixedClock(now),
            [tenantReader],
            [auditReader]);
        var fullProfile = await fullProfileService.GetSummaryAsync();

        Assert.IsTrue(fullProfile.IsSuccess);
        Assert.AreEqual(4, fullProfile.Value!.ActiveTenantCount);
        Assert.AreEqual(3, fullProfile.Value.OnlineSessionCount);
        Assert.AreEqual(12, fullProfile.Value.TodayRequestCount);
        Assert.AreEqual(0.25m, fullProfile.Value.TodayErrorRate);
        CollectionAssert.AreEqual(
            expectedActivities,
            fullProfile.Value.RecentActivities);
        Assert.AreEqual(
            new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero),
            auditReader.StartOfDayUtc);
        Assert.AreEqual(5, auditReader.RecentActivityTake);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class OnlineSessionQueryExecutor(long onlineSessionCount) : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(
                "platform.count_active_host_online_sessions",
                statement.Name);
            return Task.FromResult((T?)(object)onlineSessionCount);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingTenantMetricsReader(long activeTenantCount)
        : IHostDashboardTenantMetricsReader
    {
        public Task<long> CountActiveTenantsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(activeTenantCount);
    }

    private sealed class RecordingAuditMetricsReader(HostDashboardAuditMetrics metrics)
        : IHostDashboardAuditMetricsReader
    {
        public DateTimeOffset StartOfDayUtc { get; private set; }

        public int RecentActivityTake { get; private set; }

        public Task<HostDashboardAuditMetrics> ReadAsync(
            DateTimeOffset startOfDayUtc,
            int recentActivityTake,
            CancellationToken cancellationToken = default)
        {
            StartOfDayUtc = startOfDayUtc;
            RecentActivityTake = recentActivityTake;
            return Task.FromResult(metrics);
        }
    }
}
