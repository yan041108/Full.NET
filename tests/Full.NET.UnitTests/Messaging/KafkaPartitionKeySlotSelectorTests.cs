using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaPartitionKeySlotSelectorTests
{
    [TestMethod]
    public void XxHash64_matches_stable_reference_vectors()
    {
        Assert.AreEqual(0xef46db3751d8e999UL, KafkaPartitionKeySlotSelector.ComputeHash(""));
        Assert.AreEqual(0xd24ec4f1a98c6e5bUL, KafkaPartitionKeySlotSelector.ComputeHash("a"));
    }

    [TestMethod]
    public void Same_key_always_maps_to_same_slot()
    {
        var first = KafkaPartitionKeySlotSelector.SelectSlot("order-20260810-42", 16);

        for (var index = 0; index < 100; index++)
        {
            Assert.AreEqual(
                first,
                KafkaPartitionKeySlotSelector.SelectSlot("order-20260810-42", 16));
        }
    }

    [TestMethod]
    public void Empty_or_oversized_key_uses_quarantine_slot_zero()
    {
        Assert.AreEqual(0, KafkaPartitionKeySlotSelector.SelectSlot(null, 8));
        Assert.AreEqual(0, KafkaPartitionKeySlotSelector.SelectSlot(string.Empty, 8));
        Assert.AreEqual(0, KafkaPartitionKeySlotSelector.SelectSlot(new string('界', 100), 8));
    }
}
