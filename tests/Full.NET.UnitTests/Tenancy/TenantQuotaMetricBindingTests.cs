using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantQuotaMetricBindingTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Unbound_legacy_reservation_cannot_mutate_current_period(bool confirm)
    {
        var fixture = new Fixture();
        fixture.SetReservation();
        var result = confirm
            ? await fixture.Service.ConfirmAsync(fixture.TenantId, new("operation"))
            : await fixture.Service.ReleaseAsync(fixture.TenantId, new("operation"));
        Assert.IsFalse(result.IsSuccess);
        await fixture.Command.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(TenantQuotaReservationStatuses.Reserved)]
    [DataRow(TenantQuotaReservationStatuses.Confirmed)]
    public async Task Unbound_legacy_reservation_cannot_be_reused(string status)
    {
        var fixture = new Fixture();
        fixture.SetReservation(status: status);
        var result = await fixture.Service.ReserveAsync(fixture.TenantId,
            new(TenantQuotaMetricCodes.IdentitySeats, "operation", 1));
        Assert.IsFalse(result.IsSuccess);
        await fixture.Command.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task New_reservation_persists_the_selected_metric_identity()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ReserveAsync(fixture.TenantId,
            new(TenantQuotaMetricCodes.IdentitySeats, "operation", 1));
        Assert.IsTrue(result.IsSuccess);
        await fixture.Command.Received(1).ExecuteAsync(TenantQuotaSql.InsertReservation,
            Arg.Is<object?>(value => HasMetricId(value, fixture.Metric.Id)), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(TenantQuotaReservationStatuses.Reserved, true, false, 2)]
    [DataRow(TenantQuotaReservationStatuses.Reserved, false, true, 2)]
    [DataRow(TenantQuotaReservationStatuses.Confirmed, true, true, 0)]
    [DataRow(TenantQuotaReservationStatuses.Released, false, true, 0)]
    public async Task Completion_keeps_original_metric_across_month_and_default_changes(
        string status, bool confirm, bool expired, int writes)
    {
        var fixture = new Fixture();
        fixture.SetReservation(fixture.Metric.Id, status, expired);
        var result = confirm
            ? await fixture.Service.ConfirmAsync(fixture.TenantId, new("operation"))
            : await fixture.Service.ReleaseAsync(fixture.TenantId, new("operation"));
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("2026-09", result.Value!.PeriodKey);
        await fixture.Query.DidNotReceive().QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(
            TenantQuotaSql.FindMetric, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.Received(writes).ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        if (writes > 0)
            await fixture.Command.Received(1).ExecuteAsync(confirm ? TenantQuotaSql.ConfirmMetric : TenantQuotaSql.ReleaseMetric,
                Arg.Is<object?>(value => HasMetricId(value, fixture.Metric.Id)), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Missing_bound_metric_never_falls_back_to_current_metric()
    {
        var fixture = new Fixture();
        fixture.SetReservation(fixture.Metric.Id);
        fixture.Query.QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(TenantQuotaSql.FindBoundMetric,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((TenantQuotaMetricRecord?)null);
        var result = await fixture.Service.ReleaseAsync(fixture.TenantId, new("operation"));
        Assert.IsFalse(result.IsSuccess);
        await fixture.Command.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private static bool HasMetricId(object? value, Guid metricId) =>
        value is Dictionary<string, object?> parameters
        && parameters.TryGetValue("MetricId", out var actual) && Equals(actual, metricId);

    private sealed class Fixture
    {
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        public readonly ICommandExecutor Command = Substitute.For<ICommandExecutor>();
        public readonly IClock Clock = Substitute.For<IClock>();
        public readonly TenantQuotaMetricRecord Metric;
        public readonly TenantQuotaReservationService Service;
        public readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        public Fixture()
        {
            Clock.UtcNow.Returns(Now);
            Metric = new(Guid.NewGuid(), TenantId, TenantQuotaMetricCodes.IdentitySeats, "2026-09", 10, 0, 1, 1);
            Query.QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Metric);
            Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Service = new(Query, Command, new DapperCommandTransaction(new RecordingDbTransactionCoordinator()), Clock, Substitute.For<IIdGenerator>());
        }

        public void SetReservation(Guid? metricId = null, string status = TenantQuotaReservationStatuses.Reserved, bool expired = false) =>
            Query.QuerySingleOrDefaultAsync<TenantQuotaReservationRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new TenantQuotaReservationRecord(Guid.NewGuid(), TenantId, Metric.MetricCode, "operation", 1,
                    status, Now.AddMinutes(expired ? -5 : 5), 1, metricId));
    }
}
