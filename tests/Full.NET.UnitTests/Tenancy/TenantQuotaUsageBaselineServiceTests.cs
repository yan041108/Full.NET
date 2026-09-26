using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReconcileQuotaUsageBaseline;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantQuotaUsageBaselineServiceTests
{
    [TestMethod]
    public async Task ReconcileAsync_dry_run_reports_seat_mismatch_without_writing()
    {
        var tenantId = Guid.CreateVersion7();
        var metricId = Guid.CreateVersion7();
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var members = Substitute.For<ITenantActiveMemberCountPort>();
        var storage = Substitute.For<ITenantResourceFileStorageUsagePort>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        StubNoMissingQuotaMetrics(queries);

        queries.QueryAsync<TenantQuotaMetricRecord>(
                TenantQuotaSql.ListMetricsByMetricCode,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new TenantQuotaMetricRecord(
                    metricId,
                    tenantId,
                    TenantQuotaMetricCodes.IdentitySeats,
                    TenantQuotaDefaults.PeriodKey,
                    100,
                    0,
                    0,
                    1),
            });
        members.CountActiveMembersAsync(tenantId, Arg.Any<CancellationToken>()).Returns(3);

        var service = new TenantQuotaUsageBaselineService(queries, commands, members, storage, clock, Substitute.For<IIdGenerator>());
        var result = await service.ReconcileAsync(
            new ReconcileTenantQuotaUsageBaselineRequest(DryRun: true));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value!.CandidateCount);
        Assert.AreEqual(0, result.Value.AppliedCount);
        Assert.IsTrue(result.Value.DryRun);
        Assert.Contains(tenantId, result.Value.TenantIds);
        await commands.DidNotReceive().ExecuteAsync(
            TenantQuotaSql.UpdateMetricUsedValue,
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ReconcileAsync_apply_updates_used_value_when_no_reserved()
    {
        var tenantId = Guid.CreateVersion7();
        var metricId = Guid.CreateVersion7();
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var members = Substitute.For<ITenantActiveMemberCountPort>();
        var storage = Substitute.For<ITenantResourceFileStorageUsagePort>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        StubNoMissingQuotaMetrics(queries);

        queries.QueryAsync<TenantQuotaMetricRecord>(
                TenantQuotaSql.ListMetricsByMetricCode,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new TenantQuotaMetricRecord(
                    metricId,
                    tenantId,
                    TenantQuotaMetricCodes.IdentitySeats,
                    TenantQuotaDefaults.PeriodKey,
                    100,
                    1,
                    0,
                    2),
            });
        members.CountActiveMembersAsync(tenantId, Arg.Any<CancellationToken>()).Returns(4);
        commands.ExecuteAsync(
                TenantQuotaSql.UpdateMetricUsedValue,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var service = new TenantQuotaUsageBaselineService(queries, commands, members, storage, clock, Substitute.For<IIdGenerator>());
        var result = await service.ReconcileAsync(
            new ReconcileTenantQuotaUsageBaselineRequest(DryRun: false));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value!.CandidateCount);
        Assert.AreEqual(1, result.Value.AppliedCount);
        await commands.Received(1).ExecuteAsync(
            TenantQuotaSql.UpdateMetricUsedValue,
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ReconcileAsync_skips_metrics_with_reserved_value()
    {
        var tenantId = Guid.CreateVersion7();
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var members = Substitute.For<ITenantActiveMemberCountPort>();
        var storage = Substitute.For<ITenantResourceFileStorageUsagePort>();
        var clock = Substitute.For<IClock>();

        StubNoMissingQuotaMetrics(queries);

        queries.QueryAsync<TenantQuotaMetricRecord>(
                TenantQuotaSql.ListMetricsByMetricCode,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new TenantQuotaMetricRecord(
                    Guid.CreateVersion7(),
                    tenantId,
                    TenantQuotaMetricCodes.IdentitySeats,
                    TenantQuotaDefaults.PeriodKey,
                    100,
                    0,
                    1,
                    1),
            });

        var service = new TenantQuotaUsageBaselineService(queries, commands, members, storage, clock, Substitute.For<IIdGenerator>());
        var result = await service.ReconcileAsync(
            new ReconcileTenantQuotaUsageBaselineRequest(DryRun: true));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, result.Value!.CandidateCount);
        await members.DidNotReceive().CountActiveMembersAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ReconcileAsync_dry_run_reports_storage_mismatch_using_files_port()
    {
        var tenantId = Guid.CreateVersion7();
        var metricId = Guid.CreateVersion7();
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var members = Substitute.For<ITenantActiveMemberCountPort>();
        var storage = Substitute.For<ITenantResourceFileStorageUsagePort>();
        var clock = Substitute.For<IClock>();

        StubNoMissingQuotaMetrics(queries);

        queries.QueryAsync<TenantQuotaMetricRecord>(
                TenantQuotaSql.ListMetricsByMetricCode,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new TenantQuotaMetricRecord(
                    metricId,
                    tenantId,
                    TenantQuotaMetricCodes.FilesStorageBytes,
                    TenantQuotaDefaults.PeriodKey,
                    1_000_000,
                    0,
                    0,
                    1),
            });
        storage.SumReadyStorageBytesAsync(tenantId, Arg.Any<CancellationToken>()).Returns(4096);

        var service = new TenantQuotaUsageBaselineService(queries, commands, members, storage, clock, Substitute.For<IIdGenerator>());
        var result = await service.ReconcileAsync(
            new ReconcileTenantQuotaUsageBaselineRequest(
                DryRun: true,
                MetricCode: TenantQuotaMetricCodes.FilesStorageBytes));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value!.CandidateCount);
        await storage.Received(1).SumReadyStorageBytesAsync(tenantId, Arg.Any<CancellationToken>());
        await members.DidNotReceive().CountActiveMembersAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ReconcileAsync_rejects_unsupported_metric_code()
    {
        var service = new TenantQuotaUsageBaselineService(
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            Substitute.For<ITenantActiveMemberCountPort>(),
            Substitute.For<ITenantResourceFileStorageUsagePort>(),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        var result = await service.ReconcileAsync(
            new ReconcileTenantQuotaUsageBaselineRequest(
                DryRun: true,
                MetricCode: "ai.tokens"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            TenancyErrorCodes.QuotaUsageBaselineUnsupportedMetric,
            result.Error!.Code);
    }

    [TestMethod]
    public async Task ReconcileAsync_apply_inserts_missing_storage_metric_with_authoritative_usage()
    {
        var tenantId = Guid.CreateVersion7();
        var metricId = Guid.CreateVersion7();
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var members = Substitute.For<ITenantActiveMemberCountPort>();
        var storage = Substitute.For<ITenantResourceFileStorageUsagePort>();
        var clock = Substitute.For<IClock>();
        var ids = Substitute.For<IIdGenerator>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        ids.NewId().Returns(metricId);

        queries.QueryAsync<TenantQuotaMetricBackfillCandidate>(
                TenantQuotaSql.ListActiveTenantsMissingQuotaMetric,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(new[] { new TenantQuotaMetricBackfillCandidate(tenantId) });
        queries.QueryAsync<TenantQuotaMetricRecord>(
                TenantQuotaSql.ListMetricsByMetricCode,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TenantQuotaMetricRecord>());
        storage.SumReadyStorageBytesAsync(tenantId, Arg.Any<CancellationToken>()).Returns(2048);
        commands.ExecuteAsync(
                TenantQuotaSql.InsertMetric,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var service = new TenantQuotaUsageBaselineService(queries, commands, members, storage, clock, ids);
        var result = await service.ReconcileAsync(
            new ReconcileTenantQuotaUsageBaselineRequest(
                DryRun: false,
                MetricCode: TenantQuotaMetricCodes.FilesStorageBytes));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value!.CandidateCount);
        Assert.AreEqual(1, result.Value.AppliedCount);
        await commands.Received(1).ExecuteAsync(
            TenantQuotaSql.InsertMetric,
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
    }

    private static void StubNoMissingQuotaMetrics(IQueryExecutor queries)
    {
        queries.QueryAsync<TenantQuotaMetricBackfillCandidate>(
                TenantQuotaSql.ListActiveTenantsMissingQuotaMetric,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TenantQuotaMetricBackfillCandidate>());
    }
}
