using System.Diagnostics.Metrics;
using Full.NET.Caching.Fusion;
using Full.NET.Caching.Fusion.Serialization;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Serialization;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace Full.NET.UnitTests.Caching;

/// <summary>验证解析前保护在同步、异步及源生成路径上的字节边界和旁路观测语义。</summary>
[TestClass]
public sealed class BoundedFusionCacheSerializerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Exact_boundary_delegates_but_one_extra_byte_never_reaches_the_parser(bool asynchronous)
    {
        var inner = Substitute.For<IFusionCacheSerializer>();
        inner.Deserialize<string>(Arg.Any<byte[]>()).Returns("decoded");
        inner.DeserializeAsync<string>(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>("decoded"));
        var serializer = new BoundedFusionCacheSerializer(inner, 64);
        if (asynchronous)
        {
            Assert.AreEqual("decoded", await serializer.DeserializeAsync<string>(new byte[64]));
            await Assert.ThrowsExactlyAsync<CacheDeserializationLimitException>(async () =>
                { await serializer.DeserializeAsync<string>(new byte[65]); });
            await inner.Received(1).DeserializeAsync<string>(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
        }
        else
        {
            Assert.AreEqual("decoded", serializer.Deserialize<string>(new byte[64]));
            Assert.ThrowsExactly<CacheDeserializationLimitException>(() => serializer.Deserialize<string>(new byte[65]));
            inner.Received(1).Deserialize<string>(Arg.Any<byte[]>());
        }
    }

    [TestMethod]
    public async Task Cancelled_read_does_not_start_parsing_or_become_a_size_failure()
    {
        var inner = Substitute.For<IFusionCacheSerializer>();
        var serializer = new BoundedFusionCacheSerializer(inner, 64);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            { await serializer.DeserializeAsync<string>(new byte[65], cancelled.Token); });
        Assert.IsEmpty(inner.ReceivedCalls());
    }

    [TestMethod]
    public async Task Source_generated_payloads_preserve_the_wire_format_and_reject_extra_input_bytes()
    {
        var inner = new FullNetFusionCacheJsonSerializer([new SettingsCacheJsonTypeInfoContributor()]);
        var payload = new GridPreferenceResponse("identity.users", 2, [], 7);
        var bytes = inner.Serialize(payload);
        var serializer = new BoundedFusionCacheSerializer(inner, bytes.Length);
        CollectionAssert.AreEqual(bytes, serializer.Serialize(payload));
        Assert.AreEqual(7, serializer.Deserialize<GridPreferenceResponse>(bytes)!.Version);
        Assert.AreEqual(7, (await serializer.DeserializeAsync<GridPreferenceResponse>(bytes))!.Version);
        // JSON 尾随空白原本合法，但增加一个字节已超限，必须在进入源生成解析器前拒绝。
        await Assert.ThrowsExactlyAsync<CacheDeserializationLimitException>(async () =>
            { await serializer.DeserializeAsync<GridPreferenceResponse>([.. bytes, (byte)' ']); });
    }

    [TestMethod]
    public void Metrics_observer_failure_does_not_override_the_rejection_or_expose_payload_dimensions()
    {
        var observed = false;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, current) =>
            {
                if (instrument.Meter.Name == CacheReliabilityTelemetry.MeterName
                    && instrument.Name == "fullnet.cache.deserialization.rejections")
                    current.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            Assert.AreEqual(0, tags.Length);
            observed = true;
            throw new InvalidOperationException("fixture observer failure");
        });
        listener.Start();
        var serializer = new BoundedFusionCacheSerializer(Substitute.For<IFusionCacheSerializer>(), 1);
        var error = Assert.ThrowsExactly<CacheDeserializationLimitException>(() => serializer.Deserialize<string>([1, 2]));
        Assert.AreEqual(1, error.MaximumBytes);
        Assert.IsTrue(observed);
    }
}
