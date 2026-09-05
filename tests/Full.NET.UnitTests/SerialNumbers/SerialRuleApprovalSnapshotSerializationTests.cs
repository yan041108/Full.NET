using System.Text.Json;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Serialization;

namespace Full.NET.UnitTests.SerialNumbers;

[TestClass]
public sealed class SerialRuleApprovalSnapshotSerializationTests
{
    [TestMethod]
    public void UpdateSerialNumberRuleRequest_round_trips_through_source_generated_context()
    {
        var request = new UpdateSerialNumberRuleRequest(
            "Invoice",
            "Host invoice serial",
            SerialNumberRuleScope.Host,
            SerialNumberResetInterval.Month,
            "INV-{utc:yyyy}-{sequence:5}",
            1,
            99999,
            20,
            true,
            4);

        var json = JsonSerializer.Serialize(
            request,
            SerialNumbersJsonSerializerContext.Default.UpdateSerialNumberRuleRequest);
        var restored = JsonSerializer.Deserialize(
            json,
            SerialNumbersJsonSerializerContext.Default.UpdateSerialNumberRuleRequest);

        Assert.IsNotNull(restored);
        Assert.AreEqual(request.DisplayName, restored!.DisplayName);
        Assert.AreEqual(request.Version, restored.Version);
        Assert.AreEqual(request.Pattern, restored.Pattern);
    }
}
