using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaConsumerBufferPressureTests
{
    [TestMethod]
    public void Reaching_high_watermark_pauses_until_depth_falls_to_low_watermark()
    {
        var pressure = new KafkaConsumerBufferPressure(highWatermark: 100, lowWatermark: 60);

        for (var index = 0; index < 100; index++)
        {
            Assert.IsTrue(pressure.TryAccept());
        }

        Assert.AreEqual(100, pressure.Depth);
        Assert.IsTrue(pressure.ShouldPause);
        Assert.IsFalse(pressure.ShouldResume);
        Assert.IsFalse(pressure.TryAccept());

        pressure.OnCompleted(39);
        Assert.IsFalse(pressure.ShouldResume);

        pressure.OnCompleted();
        Assert.AreEqual(60, pressure.Depth);
        Assert.IsTrue(pressure.ShouldResume);
    }

    [TestMethod]
    public void Completion_cannot_reduce_depth_below_zero()
    {
        var pressure = new KafkaConsumerBufferPressure(highWatermark: 4, lowWatermark: 1);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            pressure.OnCompleted());

        StringAssert.Contains(exception.Message, "below zero");
    }

    [TestMethod]
    public void Constructor_rejects_invalid_hysteresis()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new KafkaConsumerBufferPressure(highWatermark: 0, lowWatermark: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new KafkaConsumerBufferPressure(highWatermark: 10, lowWatermark: 10));
    }
}
