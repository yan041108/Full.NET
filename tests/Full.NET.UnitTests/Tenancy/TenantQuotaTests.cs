using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantQuotaTests
{
    [TestMethod]
    public void ValidateReserveRequest_rejects_invalid_payload()
    {
        var result = TenantQuotaReservationService.ValidateReserveRequest(
            new ReserveTenantQuotaRequest(string.Empty, "op-1", 0));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.QuotaRequestInvalid, result.Error!.Code);
    }

    [TestMethod]
    public void ValidateReserveRequest_accepts_valid_payload()
    {
        var result = TenantQuotaReservationService.ValidateReserveRequest(
            new ReserveTenantQuotaRequest(" seats ", " op-42 ", 3));
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("seats", result.Value.MetricCode);
        Assert.AreEqual("op-42", result.Value.OperationId);
        Assert.AreEqual(3, result.Value.Amount);
    }
}
