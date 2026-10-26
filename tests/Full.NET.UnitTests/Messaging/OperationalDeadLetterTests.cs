using System.Security.Claims;
using System.Text;
using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modules.Messaging.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Messaging;

/// <summary>验证真实 DLQ 快照的重放完整性、损坏消息隔离与可信身份边界。</summary>
[TestClass]
public sealed class OperationalDeadLetterTests
{
    [TestMethod]
    public async Task Worker_catalog_probes_dispose_async_only_scoped_dependencies()
    {
        var disposed = 0;
        var services = new ServiceCollection();
        services.AddScoped(_ => new AsyncOnlyDependency(() => Interlocked.Increment(ref disposed)));
        services.AddScoped<IIntegrationEventSubscriptionCatalog>(provider =>
        {
            _ = provider.GetRequiredService<AsyncOnlyDependency>();
            return new IntegrationEventSubscriptionCatalog([], []);
        });
        await using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new KafkaMessagingOptions { Enabled = true });
        using var consumer = new KafkaConsumerWorker(options, scopes, null!, NullLogger<KafkaConsumerWorker>.Instance);
        using var projection = new KafkaDeadLetterProjectionWorker(options, scopes, new KafkaEnvelopeReader(),
            NullLogger<KafkaDeadLetterProjectionWorker>.Instance);
        await consumer.StartAsync(default);
        await consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        await projection.StartAsync(default);
        await projection.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, disposed);
    }

    private sealed class AsyncOnlyDependency(Action disposed) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { disposed(); return ValueTask.CompletedTask; }
    }

    [TestMethod]
    public void Valid_dlq_capture_preserves_original_identity_payload_and_large_source_offset()
    {
        var id = Guid.CreateVersion7();
        var message = Message(id);
        var capture = KafkaDeadLetterCapture.Create(message, new KafkaEnvelopeReader());
        Assert.IsTrue(capture.CanReplay);
        Assert.AreEqual(id, capture.MessageId);
        Assert.AreEqual(9007199254740993L, capture.SourceOffset);
        var snapshot = DeadLetterMessageSnapshot.FromJson(capture.SnapshotJson);
        var envelope = snapshot.Envelope!.ToEnvelope();
        Assert.AreEqual(id, envelope.EventId);
        Assert.AreEqual("aggregate-1", envelope.PartitionKey);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, envelope.Payload.ToArray());
        Assert.IsNull(snapshot.RawPayload);
    }

    [TestMethod]
    public void Failure_time_uses_utc_provider_bounds_before_persisting_projection()
    {
        var message = Message(Guid.CreateVersion7());
        message.Message.Headers.Add(KafkaDeliveryHeaderNames.FirstFailedAtUtc, Encoding.UTF8.GetBytes("1000-01-01T00:00:00+14:00"));
        message.Message.Headers.Add(KafkaDeliveryHeaderNames.LastFailedAtUtc, Encoding.UTF8.GetBytes("1000-01-01T00:00:00+14:00"));
        var capture = KafkaDeadLetterCapture.Create(message, new KafkaEnvelopeReader());
        Assert.IsTrue(capture.FirstFailedAtUtc.Year >= 1000);
        Assert.IsTrue(capture.LastFailedAtUtc.Year >= 1000);
        Assert.IsTrue(capture.CanReplay);
    }

    [TestMethod]
    public void Malformed_envelope_is_queryable_but_not_replayable_and_unrelated_headers_are_excluded()
    {
        var message = Message(Guid.CreateVersion7());
        message.Message.Headers.Remove(KafkaEnvelopeHeaderNames.EventId);
        message.Message.Headers.Add("Authorization", Encoding.UTF8.GetBytes("never-store-this"));
        var capture = KafkaDeadLetterCapture.Create(message, new KafkaEnvelopeReader());
        Assert.IsFalse(capture.CanReplay);
        Assert.IsNull(capture.MessageId);
        Assert.AreEqual(12L, capture.DlqOffset);
        var snapshot = DeadLetterMessageSnapshot.FromJson(capture.SnapshotJson);
        Assert.IsNull(snapshot.Envelope);
        CollectionAssert.AreEqual(message.Message.Value, snapshot.RawPayload);
        Assert.IsFalse(capture.SnapshotJson.Contains("never-store-this", StringComparison.Ordinal));
        Assert.IsFalse(snapshot.Headers.Any(h => h.Key == "Authorization"));
    }

    [TestMethod]
    public void Operator_requires_authenticated_nonempty_subject_and_explicit_bounded_reason()
    {
        var accessor = Actor();
        Assert.IsNull(MessagingOperationActor.Validate(MessagingOperationActor.Resolve(accessor), "repair verified failure"));
        Assert.IsNotNull(MessagingOperationActor.Validate(null, "reason"));
        Assert.IsNotNull(MessagingOperationActor.Validate(MessagingOperationActor.Resolve(accessor), " "));
        Assert.IsNotNull(MessagingOperationActor.Validate(MessagingOperationActor.Resolve(accessor), new string('a', 1001)));
        Assert.IsNotNull(MessagingOperationActor.Validate(MessagingOperationActor.Resolve(accessor), new string(' ', 512) + "a"));
        accessor.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString())]));
        Assert.IsNull(MessagingOperationActor.Resolve(accessor));
    }

    internal static HttpContextAccessor Actor() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", "01929d50-8640-7000-8000-000000000001"),
                new Claim(ClaimTypes.Name, "运维测试用户")], "test")),
        },
    };

    private static ConsumeResult<string, byte[]> Message(Guid id)
    {
        var headers = new Headers();
        foreach (var (key, value) in new Dictionary<string, string>
        {
            [KafkaEnvelopeHeaderNames.EventId] = id.ToString(),
            [KafkaEnvelopeHeaderNames.MessageType] = "fullnet.messaging.test.failed",
            [KafkaEnvelopeHeaderNames.SchemaVersion] = "1",
            [KafkaEnvelopeHeaderNames.ContentType] = "application/x-memorypack",
            [KafkaEnvelopeHeaderNames.Producer] = "fullnet.tests",
            [KafkaEnvelopeHeaderNames.OccurredAtUtc] = DateTimeOffset.UtcNow.ToString("O"),
            [KafkaDeliveryHeaderNames.ConsumerName] = "fullnet.messaging.test.consumer",
            [KafkaDeliveryHeaderNames.SourceTopic] = "fullnet.messaging.test.v1",
            [KafkaDeliveryHeaderNames.SourcePartition] = "0",
            [KafkaDeliveryHeaderNames.SourceOffset] = "9007199254740993",
            [KafkaDeliveryHeaderNames.FailureCode] = "messaging.permanent.test",
        }) headers.Add(key, Encoding.UTF8.GetBytes(value));
        return new ConsumeResult<string, byte[]>
        {
            Topic = "fullnet.messaging.test.v1.dlq", Partition = 0, Offset = 12,
            Message = new Message<string, byte[]> { Key = "aggregate-1", Value = [1, 2, 3], Headers = headers },
        };
    }
}
