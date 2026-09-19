using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;
using Full.NET.Modules.Tenancy.Serialization;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantQuotaOperationAddressingTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Explicit_metric_selects_the_exact_reservation(bool confirm)
    {
        var tenantId = Guid.NewGuid();
        var metricId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        query.QuerySingleOrDefaultAsync<TenantQuotaReservationRecord>(TenantQuotaSql.FindReservationByOperation,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new TenantQuotaReservationRecord(
                Guid.NewGuid(), tenantId, TenantQuotaMetricCodes.IdentitySeats, "shared", 1,
                TenantQuotaReservationStatuses.Reserved, now.AddMinutes(5), 1, metricId));
        query.QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new TenantQuotaMetricRecord(metricId, tenantId, TenantQuotaMetricCodes.IdentitySeats, "default", 10, 0, 1, 1));
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var service = new TenantQuotaReservationService(query, command,
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()), clock, Substitute.For<IIdGenerator>());
        const string json = """{"operationId":"shared","metricCode":"identity.seats"}""";
        var result = confirm
            ? await service.ConfirmAsync(tenantId, JsonSerializer.Deserialize(json, TenancyJsonSerializerContext.Default.ConfirmTenantQuotaRequest)!)
            : await service.ReleaseAsync(tenantId, JsonSerializer.Deserialize(json, TenancyJsonSerializerContext.Default.ReleaseTenantQuotaRequest)!);
        Assert.IsTrue(result.IsSuccess);
        await query.DidNotReceive().QuerySingleOrDefaultAsync<TenantQuotaReservationRecord>(
            TenantQuotaSql.FindReservationByTenantOperation, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await query.Received(1).QuerySingleOrDefaultAsync<TenantQuotaReservationRecord>(TenantQuotaSql.FindReservationByOperation,
            Arg.Is<object?>(value => HasAddress(value, tenantId)), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(true, 0)]
    [DataRow(false, 0)]
    [DataRow(true, 1)]
    [DataRow(false, 1)]
    [DataRow(true, 65)]
    [DataRow(false, 65)]
    public async Task Invalid_explicit_metric_is_rejected_before_database_access(bool confirm, int length)
    {
        var query = Substitute.For<IQueryExecutor>();
        var service = new TenantQuotaReservationService(query, Substitute.For<ICommandExecutor>(),
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()), Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
        var metric = length == 1 ? " " : new string('x', length);
        var result = confirm
            ? await service.ConfirmAsync(Guid.NewGuid(), new("shared") { MetricCode = metric })
            : await service.ReleaseAsync(Guid.NewGuid(), new("shared") { MetricCode = metric });
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.QuotaRequestInvalid, result.Error!.Code);
        await query.DidNotReceive().QuerySingleOrDefaultAsync<TenantQuotaReservationRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Seat_port_always_supplies_its_own_metric(bool confirm)
    {
        var quota = Substitute.For<ITenantQuotaReservationService>();
        var metric = new TenantQuotaMetricResponse(Guid.NewGuid(), Guid.NewGuid(), TenantQuotaMetricCodes.IdentitySeats, "default", 10, 1, 0, 1);
        quota.ConfirmAsync(Arg.Any<Guid>(), Arg.Any<ConfirmTenantQuotaRequest>(), Arg.Any<CancellationToken>())
            .Returns(Full.NET.Abstractions.Results.Result<TenantQuotaMetricResponse>.Success(metric));
        quota.ReleaseAsync(Arg.Any<Guid>(), Arg.Any<ReleaseTenantQuotaRequest>(), Arg.Any<CancellationToken>())
            .Returns(Full.NET.Abstractions.Results.Result<TenantQuotaMetricResponse>.Success(metric));
        var port = new TenantMemberSeatQuotaPort(quota);
        if (confirm)
        {
            await port.ConfirmAsync(metric.TenantId, "shared");
            await quota.Received(1).ConfirmAsync(metric.TenantId,
                Arg.Is<ConfirmTenantQuotaRequest>(request => request != null && request.MetricCode == TenantQuotaMetricCodes.IdentitySeats && request.OperationId == "shared"),
                Arg.Any<CancellationToken>());
        }
        else
        {
            await port.ReleaseAsync(metric.TenantId, "shared");
            await quota.Received(1).ReleaseAsync(metric.TenantId,
                Arg.Is<ReleaseTenantQuotaRequest>(request => request != null && request.MetricCode == TenantQuotaMetricCodes.IdentitySeats && request.OperationId == "shared"),
                Arg.Any<CancellationToken>());
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Missing_explicit_metric_does_not_fall_back_to_legacy_lookup(bool confirm)
    {
        var query = Substitute.For<IQueryExecutor>();
        var service = new TenantQuotaReservationService(query, Substitute.For<ICommandExecutor>(),
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()), Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
        var result = confirm
            ? await service.ConfirmAsync(Guid.NewGuid(), new("shared") { MetricCode = "missing" })
            : await service.ReleaseAsync(Guid.NewGuid(), new("shared") { MetricCode = "missing" });
        Assert.IsFalse(result.IsSuccess);
        await query.DidNotReceive().QuerySingleOrDefaultAsync<TenantQuotaReservationRecord>(
            TenantQuotaSql.FindReservationByTenantOperation, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private static bool HasAddress(object? value, Guid tenantId) =>
        value is Dictionary<string, object?> parameters
        && Equals(parameters["TenantId"], tenantId)
        && Equals(parameters["MetricCode"], TenantQuotaMetricCodes.IdentitySeats)
        && Equals(parameters["OperationId"], "shared");
}
