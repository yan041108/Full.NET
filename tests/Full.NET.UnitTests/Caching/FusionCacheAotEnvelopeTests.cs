using Full.NET.Caching.Fusion.Serialization;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Serialization;
using ZiggyCreatures.Caching.Fusion.Internals;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Full.NET.UnitTests.Caching;

/// <summary>验证实际 SDK L2 包装类型的源生成闭包及 JIT/Native 双向线格式兼容。</summary>
[TestClass]
public sealed class FusionCacheAotEnvelopeTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Grid_envelope_preserves_sdk_wire_format_and_nested_columns(bool asynchronous)
    {
        var payload = new GridPreferenceResponse("identity.users", 2,
            [new GridColumnPreference("employeeNumber", 0, 224, true, null)], 7);
        var entry = await VerifyEnvelopeAsync(payload, asynchronous);
        Assert.AreEqual(payload.Version, entry.Value.Version);
        CollectionAssert.AreEqual(payload.Columns.ToArray(), entry.Value.Columns.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Diagnostic_documents_have_no_cache_envelope_registration(bool asynchronous)
    {
        var serializer = new FullNetFusionCacheJsonSerializer([new SettingsCacheJsonTypeInfoContributor()]);
        var payload = new DiagnosticPolicyDocument(7, LoggingPressureState.Normal, []);
        var entry = new FusionCacheDistributedEntry<DiagnosticPolicyDocument>(payload, 1, 2, null, null);
        if (asynchronous)
            await Assert.ThrowsExactlyAsync<NotSupportedException>(async () => { await serializer.SerializeAsync(entry); });
        else
            Assert.ThrowsExactly<NotSupportedException>(() => serializer.Serialize(entry));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tag_timestamp_envelope_has_infrastructure_owned_metadata(bool asynchronous)
    {
        var entry = await VerifyEnvelopeAsync(638_000_000_000_000_000L, asynchronous);
        Assert.AreEqual(638_000_000_000_000_000L, entry.Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Unknown_envelope_still_rejects_reflection_fallback(bool asynchronous)
    {
        var serializer = new FullNetFusionCacheJsonSerializer([new SettingsCacheJsonTypeInfoContributor()]);
        var entry = new FusionCacheDistributedEntry<UnregisteredPayload>(new("unknown"), 1, 2, null, null);
        if (asynchronous)
        {
            await Assert.ThrowsExactlyAsync<NotSupportedException>(async () => { await serializer.SerializeAsync(entry); });
            await Assert.ThrowsExactlyAsync<NotSupportedException>(async () =>
                { await serializer.DeserializeAsync<FusionCacheDistributedEntry<UnregisteredPayload>>("{}"u8.ToArray()); });
        }
        else
        {
            Assert.ThrowsExactly<NotSupportedException>(() => serializer.Serialize(entry));
            Assert.ThrowsExactly<NotSupportedException>(() =>
                serializer.Deserialize<FusionCacheDistributedEntry<UnregisteredPayload>>("{}"u8.ToArray()));
        }
    }

    private static async Task<FusionCacheDistributedEntry<T>> VerifyEnvelopeAsync<T>(T payload, bool asynchronous)
    {
        var native = new FullNetFusionCacheJsonSerializer([new SettingsCacheJsonTypeInfoContributor()]);
        var jit = new FusionCacheSystemTextJsonSerializer();
        var metadata = new FusionCacheEntryMetadata(true, 600, "fixture-etag", 400, 3, 2);
        var source = new FusionCacheDistributedEntry<T>(payload, 500, 700, ["fixture-tag"], metadata);
        var expected = jit.Serialize(source);
        var actual = asynchronous ? await native.SerializeAsync(source) : native.Serialize(source);
        // HTTP 的 Web 命名规则不能改变 SDK 默认 Redis 格式，滚动混部必须双向可读。
        CollectionAssert.AreEqual(expected, actual);
        var fromJit = asynchronous
            ? await native.DeserializeAsync<FusionCacheDistributedEntry<T>>(expected)
            : native.Deserialize<FusionCacheDistributedEntry<T>>(expected);
        var fromNative = jit.Deserialize<FusionCacheDistributedEntry<T>>(actual);
        Assert.IsNotNull(fromJit);
        Assert.IsNotNull(fromNative);
        foreach (var restored in new[] { fromJit, fromNative })
        {
            Assert.AreEqual(source.Timestamp, restored.Timestamp);
            Assert.AreEqual(source.LogicalExpirationTimestamp, restored.LogicalExpirationTimestamp);
            CollectionAssert.AreEqual(source.Tags, restored.Tags);
            Assert.IsNotNull(restored.Metadata);
            Assert.AreEqual(metadata.IsStale, restored.Metadata.IsStale);
            Assert.AreEqual(metadata.ETag, restored.Metadata.ETag);
            Assert.AreEqual(metadata.LastModifiedTimestamp, restored.Metadata.LastModifiedTimestamp);
            Assert.AreEqual(metadata.EagerExpirationTimestamp, restored.Metadata.EagerExpirationTimestamp);
            Assert.AreEqual(metadata.Size, restored.Metadata.Size);
            Assert.AreEqual(metadata.Priority, restored.Metadata.Priority);
        }
        return fromJit;
    }

    private sealed record UnregisteredPayload(string Value);
}
