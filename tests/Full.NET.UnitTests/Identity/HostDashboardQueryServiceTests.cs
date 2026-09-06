using System.Security.Claims;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.GetHostDashboardSummary;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Workflow;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostDashboardQueryServiceTests
{
    [TestMethod]
    public async Task Summary_uses_owner_metrics_and_degrades_when_optional_readers_are_absent()
    {
        var now = new DateTimeOffset(2026, 7, 26, 9, 30, 0, TimeSpan.Zero);
        var queryExecutor = new OnlineSessionQueryExecutor(3);
        var principal = CreatePrincipal("identity.sessions.read");

        var reducedProfileService = new HostDashboardQueryService(
            queryExecutor,
            new FixedClock(now),
            [],
            [],
            [],
            []);
        var reducedProfile = await reducedProfileService.GetSummaryAsync(
            principal,
            CreateEvaluator(),
            Guid.CreateVersion7());

        Assert.IsTrue(reducedProfile.IsSuccess);
        Assert.IsNull(reducedProfile.Value!.ActiveTenantCount);
        Assert.AreEqual(3, reducedProfile.Value.OnlineSessionCount);
        Assert.IsNull(reducedProfile.Value.TodayRequestCount);
        Assert.IsNull(reducedProfile.Value.TodayErrorRate);
        Assert.IsNull(reducedProfile.Value.RecentActivities);
        Assert.IsNull(reducedProfile.Value.AccessTrafficTrend);
        Assert.IsEmpty(reducedProfile.Value.BusinessEntries);

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
        var trendReader = new RecordingAuditTrendReader(
            new HostDashboardTrafficTrendResponse(
                now.AddHours(-12),
                now,
                60,
                [
                    new HostDashboardTrafficTrendBucketResponse(now.AddHours(-1), 8, 1),
                ]));
        var workflowReader = new RecordingWorkflowEntryReader(
            new HostDashboardWorkflowEntryMetrics(5, 2));
        var fullPrincipal = CreatePrincipal(
            HostDashboardMetricPermissions.ActiveTenants,
            HostDashboardMetricPermissions.OnlineSessions,
            HostDashboardMetricPermissions.AuditAccess,
            HostDashboardMetricPermissions.AuditTrends,
            HostDashboardMetricPermissions.WorkflowTodos,
            HostDashboardMetricPermissions.WorkflowInstances);
        var fullProfileService = new HostDashboardQueryService(
            queryExecutor,
            new FixedClock(now),
            [tenantReader],
            [auditReader],
            [trendReader],
            [workflowReader]);
        var fullProfile = await fullProfileService.GetSummaryAsync(
            fullPrincipal,
            CreateEvaluator(),
            Guid.CreateVersion7());

        Assert.IsTrue(fullProfile.IsSuccess);
        Assert.AreEqual(4, fullProfile.Value!.ActiveTenantCount);
        Assert.AreEqual(3, fullProfile.Value.OnlineSessionCount);
        Assert.AreEqual(12, fullProfile.Value.TodayRequestCount);
        Assert.AreEqual(0.25m, fullProfile.Value.TodayErrorRate);
        CollectionAssert.AreEqual(
            expectedActivities,
            fullProfile.Value.RecentActivities);
        Assert.IsNotNull(fullProfile.Value.AccessTrafficTrend);
        Assert.AreEqual(1, fullProfile.Value.AccessTrafficTrend!.Buckets.Length);
        Assert.HasCount(2, fullProfile.Value.BusinessEntries);
        Assert.AreEqual(
            HostDashboardBusinessEntryKeys.WorkflowPendingTodos,
            fullProfile.Value.BusinessEntries[0].EntryKey);
        Assert.AreEqual(5, fullProfile.Value.BusinessEntries[0].Count);
        Assert.AreEqual(
            new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero),
            auditReader.StartOfDayUtc);
        Assert.AreEqual(5, auditReader.RecentActivityTake);
        Assert.AreEqual(now.AddHours(-12), trendReader.FromUtc);
        Assert.AreEqual(now, trendReader.ToUtc);
    }

    [TestMethod]
    public async Task Summary_omits_unauthorized_metric_fragments_without_zero_placeholders()
    {
        var now = new DateTimeOffset(2026, 7, 26, 9, 30, 0, TimeSpan.Zero);
        var principal = CreatePrincipal(HostDashboardMetricPermissions.AuditAccess);
        var service = new HostDashboardQueryService(
            new OnlineSessionQueryExecutor(9),
            new FixedClock(now),
            [new RecordingTenantMetricsReader(99)],
            [new RecordingAuditMetricsReader(
                new HostDashboardAuditMetrics(3, 0.1m, []))],
            [new RecordingAuditTrendReader(
                new HostDashboardTrafficTrendResponse(now.AddHours(-12), now, 60, []))],
            [new RecordingWorkflowEntryReader(new HostDashboardWorkflowEntryMetrics(7, 4))]);

        var result = await service.GetSummaryAsync(
            principal,
            CreateEvaluator(),
            Guid.CreateVersion7());

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.Value!.ActiveTenantCount);
        Assert.IsNull(result.Value.OnlineSessionCount);
        Assert.AreEqual(3, result.Value.TodayRequestCount);
        Assert.IsNull(result.Value.AccessTrafficTrend);
        Assert.IsEmpty(result.Value.BusinessEntries);
    }

    private static PermissionClaimEvaluator CreateEvaluator() =>
        new(AuthorizationCatalog.Create(
            [
                new IdentityAuthorizationContributor(),
                new TenancyAuthorizationContributor(),
                new AuditingAuthorizationContributor(),
                new WorkflowAuthorizationContributor(),
            ]));

    private static ClaimsPrincipal CreatePrincipal(params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(IdentityClaimTypes.Scope, "host"),
        };
        foreach (var permission in permissions)
        {
            claims.Add(new Claim(IdentityClaimTypes.Permission, permission));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "unit-test"));
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

    private sealed class RecordingAuditTrendReader(HostDashboardTrafficTrendResponse trend)
        : IHostDashboardAuditTrendReader
    {
        public DateTimeOffset FromUtc { get; private set; }

        public DateTimeOffset ToUtc { get; private set; }

        public Task<HostDashboardTrafficTrendResponse> ReadAccessTrendAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
        {
            FromUtc = fromUtc;
            ToUtc = toUtc;
            return Task.FromResult(trend);
        }
    }

    private sealed class RecordingWorkflowEntryReader(HostDashboardWorkflowEntryMetrics metrics)
        : IHostDashboardWorkflowEntryReader
    {
        public Task<HostDashboardWorkflowEntryMetrics> ReadAsync(
            Guid actorUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(metrics);
    }
}
