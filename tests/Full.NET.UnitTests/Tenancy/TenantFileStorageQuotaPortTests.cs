using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantFileStorageQuotaPortTests
{
    [TestMethod]
    public async Task TryReserveAsync_delegates_to_files_storage_bytes_metric()
    {
        var quota = Substitute.For<ITenantQuotaReservationService>();
        var tenantId = Guid.CreateVersion7();
        const string operationId = "00000000000000000000000000000001";
        const long bytes = 4096;
        quota.ReserveAsync(
                tenantId,
                Arg.Is<ReserveTenantQuotaRequest>(request =>
                    request!.MetricCode == TenantQuotaMetricCodes.FilesStorageBytes
                    && request.OperationId == operationId
                    && request.Amount == bytes),
                Arg.Any<CancellationToken>())
            .Returns(Result<ReserveTenantQuotaResponse>.Success(
                new ReserveTenantQuotaResponse(
                    Guid.CreateVersion7(),
                    tenantId,
                    TenantQuotaMetricCodes.FilesStorageBytes,
                    operationId,
                    bytes,
                    TenantQuotaReservationStatuses.Reserved,
                    DateTimeOffset.UtcNow.AddMinutes(30))));

        var port = new TenantFileStorageQuotaPort(quota);
        var result = await port.TryReserveAsync(tenantId, operationId, bytes);
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task TryReserveAsync_returns_quota_exceeded_when_limit_reached()
    {
        var quota = Substitute.For<ITenantQuotaReservationService>();
        var tenantId = Guid.CreateVersion7();
        quota.ReserveAsync(
                tenantId,
                Arg.Any<ReserveTenantQuotaRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ReserveTenantQuotaResponse>.Failure(new Error(
                TenancyErrorCodes.QuotaExceeded,
                "The tenant quota limit has been exceeded.",
                ErrorType.BusinessRule)));

        var port = new TenantFileStorageQuotaPort(quota);
        var result = await port.TryReserveAsync(tenantId, Guid.CreateVersion7().ToString("N"), 1024);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.QuotaExceeded, result.Error!.Code);
    }
}
