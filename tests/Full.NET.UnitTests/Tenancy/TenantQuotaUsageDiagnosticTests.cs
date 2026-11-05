using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantQuota;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>只读诊断使用指定企业的所有者计数，进行中和缺失值不能伪装成一致或零。</summary>
[TestClass]
public sealed class TenantQuotaUsageDiagnosticTests
{
    [TestMethod]
    [DataRow(TenantQuotaMetricCodes.IdentitySeats)]
    [DataRow(TenantQuotaMetricCodes.FilesStorageBytes)]
    public async Task Exact_observation_uses_only_requested_source_and_preserves_int64(string metric)
    {
        var f = new Fixture(long.MaxValue - 100, long.MaxValue);
        var value = (await f.Service.GetAsync(f.Tenant, metric, default)).Value!;
        Assert.AreEqual("9223372036854775707", value.BookUsedValue); Assert.AreEqual("9223372036854775807", value.AuthoritativeUsedValue);
        Assert.AreEqual("100", value.ObservedDifference); Assert.AreEqual("Different", value.State); Assert.AreEqual(f.Now, value.ReadAtUtc);
        if (metric == TenantQuotaMetricCodes.IdentitySeats)
        {
            await f.Members.Received(1).CountActiveMembersByTenantsAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Count == 1 && ids.Contains(f.Tenant)), Arg.Any<CancellationToken>());
            await f.Members.Received(2).FindUnsettledSeatTenantsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
            await f.Files.DidNotReceiveWithAnyArgs().SumReadyStorageBytesByTenantsAsync(default!, default);
        }
        else
        {
            await f.Files.Received(1).SumReadyStorageBytesByTenantsAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Count == 1 && ids.Contains(f.Tenant)), Arg.Any<CancellationToken>());
            await f.Members.DidNotReceiveWithAnyArgs().CountActiveMembersByTenantsAsync(default!, default);
        }
    }

    [TestMethod]
    [DataRow(100L, 1L, "Different", "-99")]
    [DataRow(0L, 0L, "Aligned", "0")]
    public async Task Signed_difference_and_real_zero_are_distinct(long book, long authority, string state, string difference)
    {
        var f = new Fixture(book, authority); var value = (await f.Service.GetAsync(f.Tenant, TenantQuotaMetricCodes.IdentitySeats, default)).Value!;
        Assert.AreEqual(state, value.State); Assert.AreEqual(difference, value.ObservedDifference);
    }

    [TestMethod]
    [DataRow("reserved")]
    [DataRow("unsettled-before")]
    [DataRow("unsettled-after")]
    [DataRow("version")]
    [DataRow("lifecycle")]
    public async Task In_progress_observation_never_reports_a_difference(string reason)
    {
        var f = new Fixture(1, 2);
        var before = reason == "reserved" ? f.Snapshot with { ReservedValue = 1 } : f.Snapshot;
        var after = reason == "version" ? before with { Version = 4 } : reason == "lifecycle" ? before with { TenantIsActive = false } : before;
        f.SetSnapshots(before, after);
        f.Members.FindUnsettledSeatTenantsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(reason == "unsettled-before" ? new[] { f.Tenant } : Array.Empty<Guid>(), reason == "unsettled-after" ? new[] { f.Tenant } : Array.Empty<Guid>());
        var value = (await f.Service.GetAsync(f.Tenant, TenantQuotaMetricCodes.IdentitySeats, default)).Value!;
        Assert.AreEqual("InProgress", value.State); Assert.IsNull(value.ObservedDifference); Assert.AreEqual("2", value.AuthoritativeUsedValue);
        await f.Members.Received(2).FindUnsettledSeatTenantsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Missing_book_does_not_become_zero()
    {
        var f = new Fixture(0, 2); var missing = f.Snapshot with { MetricId = null, UsedValue = null, ReservedValue = null, Version = null };
        f.SetSnapshots(missing, missing);
        var value = (await f.Service.GetAsync(f.Tenant, TenantQuotaMetricCodes.IdentitySeats, default)).Value!;
        Assert.AreEqual("Unconfigured", value.State); Assert.IsNull(value.BookUsedValue); Assert.AreEqual("2", value.AuthoritativeUsedValue); Assert.IsNull(value.ObservedDifference);
    }

    [TestMethod]
    public async Task Optional_files_without_provider_stays_unknown()
    {
        var f = new Fixture(3, 0); var service = new TenantQuotaUsageDiagnosticService(f.Queries, f.Members, f.Clock);
        var value = (await service.GetAsync(f.Tenant, TenantQuotaMetricCodes.FilesStorageBytes, default)).Value!;
        Assert.AreEqual("Unavailable", value.State); Assert.IsNull(value.AuthoritativeUsedValue); Assert.IsNull(value.ObservedDifference);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Missing_or_invalid_counter_does_not_become_zero(bool negative)
    {
        var f = new Fixture(0, 0); if (negative) f.Counts[f.Tenant] = -1; else f.Counts.Clear();
        var value = (await f.Service.GetAsync(f.Tenant, TenantQuotaMetricCodes.IdentitySeats, default)).Value!;
        Assert.AreEqual("Unavailable", value.State); Assert.IsNull(value.AuthoritativeUsedValue); Assert.IsNull(value.ObservedDifference);
    }

    [TestMethod]
    public async Task Missing_tenant_stops_before_owner_counts()
    {
        var f = new Fixture(0, 0); f.Queries.QuerySingleOrDefaultAsync<TenantQuotaDiagnosticSnapshot>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((TenantQuotaDiagnosticSnapshot?)null);
        var result = await f.Service.GetAsync(f.Tenant, TenantQuotaMetricCodes.IdentitySeats, default);
        Assert.AreEqual(ErrorType.NotFound, result.Error!.Type); await f.Members.DidNotReceiveWithAnyArgs().CountActiveMembersByTenantsAsync(default!, default);
    }

    [TestMethod]
    public async Task Invalid_inputs_do_not_reach_persistence()
    {
        var f = new Fixture(0, 0);
        Assert.AreEqual(ErrorType.NotFound, (await f.Service.GetAsync(Guid.Empty, TenantQuotaMetricCodes.IdentitySeats, default)).Error!.Type);
        Assert.AreEqual(ErrorType.Validation, (await f.Service.GetAsync(f.Tenant, "arbitrary.metric", default)).Error!.Type);
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }

    private sealed class Fixture
    {
        internal readonly Guid Tenant = Guid.CreateVersion7(); internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>(); internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly ITenantActiveMemberCountPort Members = Substitute.For<ITenantActiveMemberCountPort>();
        internal readonly ITenantResourceFileStorageUsagePort Files = Substitute.For<ITenantResourceFileStorageUsagePort>();
        internal readonly Dictionary<Guid, long> Counts; internal readonly TenantQuotaDiagnosticSnapshot Snapshot;
        internal readonly TenantQuotaUsageDiagnosticService Service;
        internal Fixture(long book, long authority)
        {
            Clock.UtcNow.Returns(Now); Counts = new() { [Tenant] = authority }; Snapshot = new(Tenant, true, Guid.CreateVersion7(), book, 0, 3);
            SetSnapshots(Snapshot, Snapshot);
            Members.FindUnsettledSeatTenantsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
            Members.CountActiveMembersByTenantsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Counts);
            Files.SumReadyStorageBytesByTenantsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Counts);
            Service = new(Queries, Members, Clock, Files);
        }
        internal void SetSnapshots(TenantQuotaDiagnosticSnapshot before, TenantQuotaDiagnosticSnapshot after) =>
            Queries.QuerySingleOrDefaultAsync<TenantQuotaDiagnosticSnapshot>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(before, after);
    }
}
