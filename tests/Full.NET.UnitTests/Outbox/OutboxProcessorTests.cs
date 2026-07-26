using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Host.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using global::MessagePack;

namespace Full.NET.UnitTests.Outbox;

[TestClass]
[DoNotParallelize]
public sealed class OutboxProcessorTests
{
    [TestMethod]
    public async Task ProcessOnceAsync_DispatchesOnlyExactTypeAndVersionThenMarksProcessed()
    {
        var message = CreateMessage(attempts: 1);
        var store = CreateStore(message);
        var matching = new RecordingHandler(message.MessageType, message.SchemaVersion);
        var wrongType = new RecordingHandler("another.event", message.SchemaVersion);
        var wrongVersion = new RecordingHandler(message.MessageType, message.SchemaVersion + 1);
        var now = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, matching, wrongType, wrongVersion);
        var processor = CreateProcessor(provider, now);

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
        await store.DidNotReceiveWithAnyArgs().MarkDeadLetterAsync(
            default,
            default,
            string.Empty,
            string.Empty,
            default,
            default);
    }

    [TestMethod]
    public async Task ProcessOnceAsync_OnHandlerFailureMarksRetryWithFutureBackoff()
    {
        var message = CreateMessage(attempts: 3);
        var store = CreateStore(message);
        var handler = new ThrowingHandler(message.MessageType, message.SchemaVersion);
        var now = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, handler);
        var processor = CreateProcessor(provider, now);

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
        await store.DidNotReceiveWithAnyArgs().MarkDeadLetterAsync(
            default,
            default,
            string.Empty,
            string.Empty,
            default,
            default);
    }

    [TestMethod]
    public async Task ProcessOnceAsync_DispatchesLegacyAliasTypeToCanonicalHandler()
    {
        const string canonicalType = "fullnet.tenancy.tenant.provisioned";
        const string legacyType = "fullnet.tenancy.tenant-provisioned";
        var message = CreateMessage(
            attempts: 1,
            messageType: legacyType);
        var store = CreateStore(message);
        var handler = new LegacyAliasHandler(canonicalType, legacyType);
        var now = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, handler);
        var processor = CreateProcessor(provider, now);

        await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.AreEqual(1, handler.HandledCount);
        await store.Received(1).MarkProcessedAsync(
            message.Id,
            message.LockId,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ProcessOnceAsync_OnUnknownEventTypeDeadLettersMessageAndContinuesBatch()
    {
        var deadLetter = CreateMessage(attempts: 2, messageType: "fullnet.unknown.event");
        var next = CreateMessage(attempts: 1);
        var store = CreateStore(deadLetter, next);
        var handler = new RecordingHandler(next.MessageType, next.SchemaVersion);
        var now = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, handler);
        var processor = CreateProcessor(provider, now);

        await processor.ProcessOnceAsync(CancellationToken.None);

        await store.Received(1).MarkDeadLetterAsync(
            deadLetter.Id,
            deadLetter.LockId,
            Arg.Is<string>(error => !string.IsNullOrWhiteSpace(error)),
            OutboxDeadLetterReasons.HandlerNotFound,
            Arg.Is<DateTimeOffset>(deadLetteredAt => deadLetteredAt == now),
            Arg.Any<CancellationToken>());
        await store.Received(1).MarkProcessedAsync(
            next.Id,
            next.LockId,
            Arg.Any<CancellationToken>());
        Assert.AreEqual(1, handler.HandledCount);
    }

    [TestMethod]
    public async Task ProcessOnceAsync_OnInvalidPayloadDeadLettersMessageImmediately()
    {
        var message = CreateMessage(attempts: 1);
        var store = CreateStore(message);
        var handler = new PoisonPayloadHandler(message.MessageType, message.SchemaVersion);
        var now = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, handler);
        var processor = CreateProcessor(provider, now);

        await processor.ProcessOnceAsync(CancellationToken.None);

        await store.Received(1).MarkDeadLetterAsync(
            message.Id,
            message.LockId,
            Arg.Is<string>(error => !string.IsNullOrWhiteSpace(error)),
            OutboxDeadLetterReasons.InvalidPayload,
            Arg.Is<DateTimeOffset>(deadLetteredAt => deadLetteredAt == now),
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().MarkFailedAsync(
            default,
            default,
            string.Empty,
            default,
            default);
    }

    [TestMethod]
    public async Task ProcessOnceAsync_WhenMaxAttemptsReachedDeadLettersTransientFailure()
    {
        var options = new OutboxWorkerOptions
        {
            MaxAttempts = 3,
        };
        var message = CreateMessage(attempts: options.MaxAttempts);
        var store = CreateStore(message);
        var handler = new ThrowingHandler(message.MessageType, message.SchemaVersion);
        var now = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, handler);
        var processor = CreateProcessor(provider, now, options);

        await processor.ProcessOnceAsync(CancellationToken.None);

        await store.Received(1).MarkDeadLetterAsync(
            message.Id,
            message.LockId,
            Arg.Is<string>(error => !string.IsNullOrWhiteSpace(error)),
            OutboxDeadLetterReasons.MaxAttemptsExceeded,
            Arg.Is<DateTimeOffset>(deadLetteredAt => deadLetteredAt == now),
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().MarkFailedAsync(
            default,
            default,
            string.Empty,
            default,
            default);
    }

    [TestMethod]
    public async Task ProcessOnceAsync_UsesConfiguredBatchAndLeaseOptions()
    {
        var options = new OutboxWorkerOptions
        {
            BatchSize = 7,
            LeaseSeconds = 45,
        };
        var message = CreateMessage(attempts: 1);
        var store = CreateStore(message);
        var handler = new RecordingHandler(message.MessageType, message.SchemaVersion);
        var now = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, handler);
        var processor = CreateProcessor(provider, now, options);

        await processor.ProcessOnceAsync(CancellationToken.None);

        await store.Received(1).AcquireAsync(
            options.BatchSize,
            TimeSpan.FromSeconds(options.LeaseSeconds),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ProcessOnceAsync_RecordsPendingCountAndOldestMessageAge()
    {
        var now = new DateTimeOffset(2026, 7, 26, 0, 2, 0, TimeSpan.Zero);
        var store = CreateStore();
        BacklogReader(store)
            .ReadBacklogAsync(Arg.Any<CancellationToken>())
            .Returns(new OutboxBacklogSnapshot(2, now.AddSeconds(-90)));
        var measurements = new List<(string Name, double Value)>();
        using var listener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, currentListener) =>
            {
                if (instrument.Meter.Name == OutboxBacklogTelemetry.MeterName)
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
                measurements.Add((instrument.Name, measurement)));
        listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, _, _) =>
                measurements.Add((instrument.Name, measurement)));
        listener.Start();
        await using var provider = CreateProvider(store);
        var processor = CreateProcessor(provider, now);

        await processor.ProcessOnceAsync(CancellationToken.None);

        CollectionAssert.Contains(
            measurements,
            ("fullnet.outbox.backlog.messages", 2d));
        CollectionAssert.Contains(
            measurements,
            ("fullnet.outbox.backlog.oldest_age", 90d));
    }

    [TestMethod]
    public async Task ProcessOnceAsync_WhenBacklogSamplingFailsStillProcessesMessages()
    {
        var message = CreateMessage(attempts: 1);
        var store = CreateStore(message);
        BacklogReader(store)
            .ReadBacklogAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OutboxBacklogSnapshot>(
                new InvalidOperationException("Backlog sampling failed.")));
        var handler = new RecordingHandler(message.MessageType, message.SchemaVersion);
        var now = new DateTimeOffset(2026, 7, 26, 0, 2, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, handler);
        var processor = CreateProcessor(provider, now);

        await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.AreEqual(1, handler.HandledCount);
        await store.Received(1).MarkProcessedAsync(
            message.Id,
            message.LockId,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ProcessOnceAsync_WithinBacklogSampleIntervalReadsSnapshotOnlyOnce()
    {
        var now = new DateTimeOffset(2026, 7, 26, 0, 2, 0, TimeSpan.Zero);
        var store = CreateStore();
        var options = new OutboxWorkerOptions
        {
            BacklogSampleSeconds = 60,
        };
        await using var provider = CreateProvider(store);
        var processor = CreateProcessor(provider, now, options);

        await processor.ProcessOnceAsync(CancellationToken.None);
        await processor.ProcessOnceAsync(CancellationToken.None);

        await BacklogReader(store).Received(1).ReadBacklogAsync(
            Arg.Any<CancellationToken>());
        await store.Received(2).AcquireAsync(
            options.BatchSize,
            TimeSpan.FromSeconds(options.LeaseSeconds),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void OutboxWorkerOptionsValidator_RejectsUnsafeBacklogSampleInterval()
    {
        var options = new OutboxWorkerOptions
        {
            BacklogSampleSeconds = 4,
        };
        var validator = new OutboxWorkerOptionsValidator();

        var result = validator.Validate(Options.DefaultName, options);

        Assert.IsFalse(result.Succeeded);
        CollectionAssert.Contains(
            (result.Failures ?? []).ToArray(),
            "OutboxWorker:BacklogSampleSeconds must be between 5 and 3600.");
    }

    private static OutboxProcessor CreateProcessor(
        ServiceProvider provider,
        DateTimeOffset now,
        OutboxWorkerOptions? options = null) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        new FixedClock(now),
        Options.Create(options ?? new OutboxWorkerOptions()),
        NullLogger<OutboxProcessor>.Instance);

    private static IOutboxStore CreateStore(params OutboxEnvelope[] messages)
    {
        var store = Substitute.For<IOutboxStore, IOutboxBacklogReader>();
        store
            .AcquireAsync(
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutboxEnvelope>>(messages));
        BacklogReader(store)
            .ReadBacklogAsync(Arg.Any<CancellationToken>())
            .Returns(new OutboxBacklogSnapshot(0, null));
        return store;
    }

    private static ServiceProvider CreateProvider(
        IOutboxStore store,
        params IIntegrationEventHandler[] handlers)
    {
        var services = new ServiceCollection();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddSingleton(store);
        services.AddSingleton(BacklogReader(store));
        foreach (var handler in handlers)
        {
            services.AddSingleton(handler);
        }

        return services.BuildServiceProvider();
    }

    private static IOutboxBacklogReader BacklogReader(IOutboxStore store) =>
        (IOutboxBacklogReader)store;

    private static OutboxEnvelope CreateMessage(
        int attempts,
        string messageType = "fullnet.test.event",
        int schemaVersion = 1,
        string contentType = "application/x-msgpack") => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        messageType,
        schemaVersion,
        contentType,
        Guid.CreateVersion7(),
        "0123456789abcdef0123456789abcdef",
        [1, 2, 3, 4],
        attempts,
        new DateTimeOffset(2026, 7, 16, 23, 59, 0, TimeSpan.Zero));

    private sealed class LegacyAliasHandler(string canonicalType, string legacyType)
        : IIntegrationEventHandler
    {
        public string EventType => canonicalType;

        public IReadOnlyList<string> LegacyEventTypes => [legacyType];

        public int SchemaVersion => 1;

        public int HandledCount { get; private set; }

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            HandledCount++;
            return Task.CompletedTask;
        }
    }

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

    private sealed class PoisonPayloadHandler(string eventType, int schemaVersion)
        : IIntegrationEventHandler
    {
        public string EventType => eventType;

        public int SchemaVersion => schemaVersion;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            throw new MessagePackSerializationException("Bad payload.");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
