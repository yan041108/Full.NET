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
public sealed class TenantQuotaReplayTests
{
    [TestMethod]
    [DataRow(TenantQuotaReservationStatuses.Reserved, false, 1L, true)]
    [DataRow(TenantQuotaReservationStatuses.Reserved, false, 2L, false)]
    [DataRow(TenantQuotaReservationStatuses.Reserved, true, 1L, false)]
    [DataRow(TenantQuotaReservationStatuses.Released, false, 1L, false)]
    [DataRow(TenantQuotaReservationStatuses.Confirmed, true, 1L, true)]
    [DataRow(TenantQuotaReservationStatuses.Confirmed, false, 2L, false)]
    [DataRow(TenantQuotaReservationStatuses.Expired, false, 1L, false)]
    public async Task Reserve_replay_requires_same_amount_and_usable_reservation(
        string status, bool expired, long amount, bool success)
    {
        var fixture = new Fixture(status, expired);
        var result = await fixture.Service.ReserveAsync(fixture.TenantId,
            new ReserveTenantQuotaRequest(TenantQuotaMetricCodes.IdentitySeats, "operation-1", amount));
        Assert.AreEqual(success, result.IsSuccess);
        await fixture.Command.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(TenantQuotaReservationStatuses.Confirmed, true, true, true, 0)]
    [DataRow(TenantQuotaReservationStatuses.Released, true, false, true, 0)]
    [DataRow(TenantQuotaReservationStatuses.Released, false, true, false, 0)]
    [DataRow(TenantQuotaReservationStatuses.Confirmed, false, false, false, 0)]
    [DataRow(TenantQuotaReservationStatuses.Reserved, true, true, false, 0)]
    [DataRow(TenantQuotaReservationStatuses.Reserved, true, false, false, 0)]
    [DataRow(TenantQuotaReservationStatuses.Expired, false, true, false, 0)]
    [DataRow(TenantQuotaReservationStatuses.Expired, false, false, false, 0)]
    public async Task Completion_replay_preserves_terminal_state_and_expired_pending_still_fails(
        string status, bool expired, bool confirm, bool success, int writes)
    {
        var fixture = new Fixture(status, expired);
        var result = confirm
            ? await fixture.Service.ConfirmAsync(fixture.TenantId, new ConfirmTenantQuotaRequest("operation-1"))
            : await fixture.Service.ReleaseAsync(fixture.TenantId, new ReleaseTenantQuotaRequest("operation-1"));
        Assert.AreEqual(success, result.IsSuccess);
        await fixture.Command.Received(writes).ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(success ? 1 : 0, fixture.Coordinator.CommitCount);
        Assert.AreEqual(success ? 0 : 1, fixture.Coordinator.RollbackCount);
    }

    private sealed class Fixture
    {
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly ICommandExecutor Command = Substitute.For<ICommandExecutor>();
        public readonly RecordingDbTransactionCoordinator Coordinator = new();
        public readonly TenantQuotaReservationService Service;

        public Fixture(string status, bool expired)
        {
            var now = DateTimeOffset.UtcNow;
            var query = Substitute.For<IQueryExecutor>();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(now);
            var ids = Substitute.For<IIdGenerator>();
            ids.NewId().Returns(_ => Guid.NewGuid());
            query.QuerySingleOrDefaultAsync<TenantQuotaReservationRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new TenantQuotaReservationRecord(Guid.NewGuid(), TenantId, TenantQuotaMetricCodes.IdentitySeats,
                    "operation-1", 1, status, now.AddMinutes(expired ? -1 : 5), 1));
            query.QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new TenantQuotaMetricRecord(Guid.NewGuid(), TenantId, TenantQuotaMetricCodes.IdentitySeats,
                    TenantQuotaDefaults.PeriodKey, 10, 0, 1, 1));
            Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Service = new TenantQuotaReservationService(query, Command, new DapperCommandTransaction(Coordinator), clock, ids);
        }
    }
}
