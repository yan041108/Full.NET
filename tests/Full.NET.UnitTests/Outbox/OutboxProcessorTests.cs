using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Host.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Outbox;

[TestClass]
public sealed class OutboxProcessorTests
{
    [TestMethod]
    public async Task ProcessOnceAsync_DispatchesOnlyExactTypeAndVersionThenMarksProcessed()
    {
        var message = CreateMessage(attempts: 1);
        var store = CreateStore(message);
        var matching = new RecordingHandler(message.Type, message.SchemaVersion);
        var wrongType = new RecordingHandler("another.event", message.SchemaVersion);
        var wrongVersion = new RecordingHandler(message.Type, message.SchemaVersion + 1);
        var now = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, matching, wrongType, wrongVersion);
        var processor = new OutboxProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(now),
            NullLogger<OutboxProcessor>.Instance);

        await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.AreEqual(1, matching.HandledCount);
        Assert.AreEqual(0, wrongType.HandledCount);
        Assert.AreEqual(0, wrongVersion.HandledCount);
        CollectionAssert.AreEqual(message.Payload, matching.LastPayload.ToArray());
        await store.Received(1).MarkProcessedAsync(
            message.Id,
            message.LockId,
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().MarkFailedAsync(
            default,
            default,
            string.Empty,
            default,
            default);
    }

    [TestMethod]
    public async Task ProcessOnceAsync_OnHandlerFailureMarksRetryWithFutureBackoff()
    {
        var message = CreateMessage(attempts: 3);
        var store = CreateStore(message);
        var handler = new ThrowingHandler(message.Type, message.SchemaVersion);
        var now = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, handler);
        var processor = new OutboxProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(now),
            NullLogger<OutboxProcessor>.Instance);

        await processor.ProcessOnceAsync(CancellationToken.None);

        await store.Received(1).MarkFailedAsync(
            message.Id,
            message.LockId,
            Arg.Is<string>(error => !string.IsNullOrWhiteSpace(error)),
            Arg.Is<DateTimeOffset>(retryAt => retryAt > now),
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().MarkProcessedAsync(
            default,
            default,
            default);
    }

    private static IOutboxStore CreateStore(OutboxEnvelope message)
    {
        var store = Substitute.For<IOutboxStore>();
        store
            .AcquireAsync(
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutboxEnvelope>>([message]));
        return store;
    }

    private static ServiceProvider CreateProvider(
        IOutboxStore store,
        params IIntegrationEventHandler[] handlers)
    {
        var services = new ServiceCollection();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddSingleton(store);
        foreach (var handler in handlers)
        {
            services.AddSingleton(handler);
        }

        return services.BuildServiceProvider();
    }

    private static OutboxEnvelope CreateMessage(int attempts) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "fullnet.test.event",
        1,
        "application/x-msgpack",
        Guid.CreateVersion7(),
        "0123456789abcdef0123456789abcdef",
        [1, 2, 3, 4],
        attempts,
        new DateTimeOffset(2026, 7, 16, 23, 59, 0, TimeSpan.Zero));

    private sealed class RecordingHandler(string eventType, int schemaVersion)
        : IIntegrationEventHandler
    {
        public string EventType => eventType;

        public int SchemaVersion => schemaVersion;

        public int HandledCount { get; private set; }

        public ReadOnlyMemory<byte> LastPayload { get; private set; }

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            HandledCount++;
            LastPayload = payload.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler(string eventType, int schemaVersion)
        : IIntegrationEventHandler
    {
        public string EventType => eventType;

        public int SchemaVersion => schemaVersion;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Handler failed.");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
