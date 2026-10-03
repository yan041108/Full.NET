using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantFileStorageQuotaPortTests
{
    /// <summary>相同机器码的 CAS 冲突仍是未知结果，不能让 Worker 撤销并发赢家的文件。</summary>
    [TestMethod]
    public async Task Confirm_conflict_is_not_authoritative_rejection()
    {
        var quota = Substitute.For<ITenantQuotaReservationService>();
        quota.ConfirmAsync(Arg.Any<Guid>(), Arg.Any<ConfirmTenantQuotaRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<TenantQuotaMetricResponse>.Failure(new Error(TenancyErrorCodes.QuotaReservationNotFound, "Concurrent confirmation", ErrorType.Conflict)));
        var result = await new TenantFileStorageQuotaPort(quota).ConfirmAsync(Guid.NewGuid(), "operation");
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.Conflict, result.Error!.Type);
    }

    /// <summary>已确认或缺失的预留无法释放，属于确定终态，不能无限阻塞物理清理。</summary>
    [TestMethod]
    public async Task Release_terminal_reservation_returns_false()
    {
        var quota = Substitute.For<ITenantQuotaReservationService>();
        quota.ReleaseAsync(Arg.Any<Guid>(), Arg.Any<ReleaseTenantQuotaRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<TenantQuotaMetricResponse>.Failure(new Error(TenancyErrorCodes.QuotaReservationNotFound, "Already confirmed", ErrorType.NotFound)));
        var result = await new TenantFileStorageQuotaPort(quota).ReleaseAsync(Guid.NewGuid(), "operation");
        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value);
    }
    /// <summary>缺失或过期预留属于明确拒绝；Files 必须区分此结果和暂时失败。</summary>
    [TestMethod]
    public async Task Confirm_missing_reservation_returns_false_instead_of_transient_failure()
    {
        var quota = Substitute.For<ITenantQuotaReservationService>();
        quota.ConfirmAsync(Arg.Any<Guid>(), Arg.Any<ConfirmTenantQuotaRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<TenantQuotaMetricResponse>.Failure(new Error(TenancyErrorCodes.QuotaReservationNotFound, "Expired", ErrorType.NotFound)));
        var result = await new TenantFileStorageQuotaPort(quota).ConfirmAsync(Guid.NewGuid(), "operation");
        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value);
    }
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
