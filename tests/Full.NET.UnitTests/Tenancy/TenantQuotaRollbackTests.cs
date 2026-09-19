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
public sealed class TenantQuotaRollbackTests
{
    [TestMethod]
    [DataRow("reserve")]
    [DataRow("confirm")]
    [DataRow("release")]
    public async Task Concurrent_update_failure_rolls_back_prior_quota_write(string operation)
    {
        var tenantId = Guid.NewGuid();
        var metricId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.NewGuid());
        var coordinator = new RecordingDbTransactionCoordinator();
        var service = new TenantQuotaReservationService(query, command,
            new DapperCommandTransaction(coordinator), clock, ids);
        query.QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new TenantQuotaMetricRecord(metricId, tenantId, "identity.seats", "all", 10, 0, 1, 1));
        if (operation != "reserve")
        {
            query.QuerySingleOrDefaultAsync<TenantQuotaReservationRecord>(
                    Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new TenantQuotaReservationRecord(Guid.NewGuid(), tenantId, "identity.seats",
                    "operation-1", 1, TenantQuotaReservationStatuses.Reserved, now.AddMinutes(10), 1, metricId));
        }

        // 首条写入成功，第二条版本条件更新失败；必须回滚已插入预留或已更新的用量。
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1, 0);
        bool success;
        if (operation == "reserve")
            success = (await service.ReserveAsync(tenantId, new ReserveTenantQuotaRequest("identity.seats", "operation-1", 1))).IsSuccess;
        else if (operation == "confirm")
            success = (await service.ConfirmAsync(tenantId, new ConfirmTenantQuotaRequest("operation-1"))).IsSuccess;
        else
            success = (await service.ReleaseAsync(tenantId, new ReleaseTenantQuotaRequest("operation-1"))).IsSuccess;

        Assert.IsFalse(success);
        await command.Received(2).ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, coordinator.CommitCount);
        Assert.AreEqual(1, coordinator.RollbackCount);
    }
}
