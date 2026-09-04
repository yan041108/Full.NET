using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;

namespace Full.NET.UnitTests.SerialNumbers;

[TestClass]
public sealed class SerialNumberPreviewServiceTests
{
    [TestMethod]
    public void Preview_is_deterministic_and_does_not_require_a_counter()
    {
        var service = new SerialNumberPreviewService();
        var request = new PreviewSerialNumberRequest(
            SerialNumberRuleScope.Tenant,
            "INV-{utc:yyyy}-{tenant}-{sequence:4}",
            "acme",
            7,
            new DateTimeOffset(2026, 7, 30, 8, 9, 10, TimeSpan.Zero),
            SerialNumberResetInterval.Never);

        var first = service.Preview(request);
        var second = service.Preview(request);

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsSuccess);
        Assert.AreEqual("INV-2026-acme-0007", first.Value!.Value);
        Assert.AreEqual(first.Value, second.Value);
        Assert.AreEqual("all", first.Value.ResetBucket);
        Assert.AreEqual(7L, first.Value.SequenceValue);
    }

    [TestMethod]
    public void Preview_rejects_a_sequence_outside_pattern_capacity()
    {
        var service = new SerialNumberPreviewService();

        var result = service.Preview(new PreviewSerialNumberRequest(
            SerialNumberRuleScope.Host,
            "{sequence:2}",
            null,
            100,
            new DateTimeOffset(2026, 7, 30, 8, 9, 10, TimeSpan.Zero),
            SerialNumberResetInterval.Never));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            SerialNumberErrorCodes.PatternInvalid,
            result.Error!.Code);
    }
}
