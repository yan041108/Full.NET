using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantMemberSeatQuotaPortTests
{
    [TestMethod]
    public async Task TryReserveAsync_delegates_to_identity_seats_metric()
    {
        var quota = Substitute.For<ITenantQuotaReservationService>();
        var tenantId = Guid.CreateVersion7();
        const string operationId = "00000000-0000-7000-8000-000000000099";
        quota.ReserveAsync(
                tenantId,
                Arg.Is<ReserveTenantQuotaRequest>(request =>
                    request!.MetricCode == TenantQuotaMetricCodes.IdentitySeats
                    && request.OperationId == operationId
                    && request.Amount == 1),
                Arg.Any<CancellationToken>())
            .Returns(Result<ReserveTenantQuotaResponse>.Success(
                new ReserveTenantQuotaResponse(
                    Guid.CreateVersion7(),
                    tenantId,
                    TenantQuotaMetricCodes.IdentitySeats,
                    operationId,
                    1,
                    TenantQuotaReservationStatuses.Reserved,
                    DateTimeOffset.UtcNow.AddMinutes(30))));

        var port = new TenantMemberSeatQuotaPort(quota);
        var result = await port.TryReserveAsync(tenantId, operationId);
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task TryReserveAsync_returns_quota_exceeded_when_limit_reached()
    {
        var quota = Substitute.For<ITenantQuotaReservationService>();
        var tenantId = Guid.CreateVersion7();
        var reserveCalls = 0;
        quota.ReserveAsync(
                tenantId,
                Arg.Any<ReserveTenantQuotaRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                reserveCalls++;
                return reserveCalls == 1
                    ? Result<ReserveTenantQuotaResponse>.Success(
                        new ReserveTenantQuotaResponse(
                            Guid.CreateVersion7(),
                            tenantId,
                            TenantQuotaMetricCodes.IdentitySeats,
                            "op-1",
                            1,
                            TenantQuotaReservationStatuses.Reserved,
                            DateTimeOffset.UtcNow.AddMinutes(30)))
                    : Result<ReserveTenantQuotaResponse>.Failure(new Error(
                        TenancyErrorCodes.QuotaExceeded,
                        "The tenant quota limit has been exceeded.",
                        ErrorType.BusinessRule));
            });

        var port = new TenantMemberSeatQuotaPort(quota);
        var first = await port.TryReserveAsync(tenantId, Guid.CreateVersion7().ToString("D"));
        var second = await port.TryReserveAsync(tenantId, Guid.CreateVersion7().ToString("D"));
        Assert.IsTrue(first.IsSuccess);
        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.QuotaExceeded, second.Error!.Code);
    }
}