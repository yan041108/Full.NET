using System.Collections.Concurrent;
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
        Assert.AreEqual(
            new IntegrationEventContext(
                message.Id,
                message.MessageType,
                message.SchemaVersion,
                message.TenantId,
                message.TraceId,
                message.OccurredAtUtc),
            matching.LastContext);
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
        var next = CreateMessage(attempts: 1, lockId: deadLetter.LockId);
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
    public async Task ProcessOnceAsync_RenewsLeaseWhileHandlerIsRunning()
    {
        var options = new OutboxWorkerOptions
        {
            LeaseSeconds = 6,
            LeaseRenewalSeconds = 1,
        };
        var message = CreateMessage(attempts: 1);
        var store = CreateStore(message);
        var handler = new BlockingHandler(
            message.MessageType,
            message.SchemaVersion);
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, handler);
        var processor = CreateProcessor(provider, now, options);

        var processingTask = processor.ProcessOnceAsync(CancellationToken.None);
        await handler.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        await store.Received().RenewLeaseAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(
                messageIds => ContainsOnly(messageIds, message.Id)),
            message.LockId,
            TimeSpan.FromSeconds(options.LeaseSeconds),
            Arg.Any<CancellationToken>());
        handler.Release();
        Assert.AreEqual(
            1,
            await processingTask.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public async Task ProcessOnceAsync_RenewsLeaseFromIndependentScopedStore()
    {
        var options = new OutboxWorkerOptions
        {
            LeaseSeconds = 6,
            LeaseRenewalSeconds = 1,
        };
        var message = CreateMessage(attempts: 1);
        var store = CreateStore(message);
        var handler = new BlockingHandler(
            message.MessageType,
            message.SchemaVersion);
        var storeScopeIds = new ConcurrentQueue<Guid>();
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateScopedStoreProvider(
            store,
            storeScopeIds,
            handler);
        var processor = CreateProcessor(provider, now, options);

        var processingTask = processor.ProcessOnceAsync(CancellationToken.None);
        await handler.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        Assert.IsGreaterThanOrEqualTo(
            2,
            storeScopeIds.Distinct().Count(),
            "领取和主动续租必须解析自不同的依赖注入 Scope。");
        handler.Release();
        Assert.AreEqual(
            1,
            await processingTask.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public async Task ProcessOnceAsync_WhenLeaseRenewalFailsCancelsHandlerAndPropagatesFailure()
    {
        var options = new OutboxWorkerOptions
        {
            LeaseSeconds = 6,
            LeaseRenewalSeconds = 1,
        };
        var message = CreateMessage(attempts: 1);
        var store = CreateStore(message);
        store
            .RenewLeaseAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(
                    messageIds => ContainsOnly(messageIds, message.Id)),
                message.LockId,
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(
                new InvalidOperationException("Lease renewal failed.")));
        var handler = new BlockingHandler(
            message.MessageType,
            message.SchemaVersion);
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, handler);
        var processor = CreateProcessor(provider, now, options);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => processor.ProcessOnceAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(3)));

        Assert.AreEqual("Lease renewal failed.", exception.Message);
        await handler.Canceled.WaitAsync(TimeSpan.FromSeconds(2));
        await store.DidNotReceiveWithAnyArgs().MarkFailedAsync(
            default,
            default,
            string.Empty,
            default,
            default);
    }

    [TestMethod]
    public async Task ProcessBatchWithLeaseRenewalAsync_WhenRenewalCallFailsBeforeTerminalPropagatesAfterScopeDisposal()
    {
        var options = new OutboxWorkerOptions
        {
            LeaseSeconds = 6,
            LeaseRenewalSeconds = 1,
        };
        var message = CreateMessage(attempts: 1);
        var store = CreateStore(message);
        var renewalCallFailed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        store
            .RenewLeaseAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                message.LockId,
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(
                _ =>
                {
                    renewalCallFailed.TrySetResult();
                    return Task.FromException(
                        new OutboxLeaseLostException(message.LockId));
                });
        var scopeDisposal = new BlockingAsyncScopeDisposal();
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        await using var provider =
            CreateRenewalScopeProvider(store, scopeDisposal);
        var processor = CreateProcessor(provider, now, options);

        var processingTask = processor.ProcessBatchWithLeaseRenewalAsync(
            [message],
            async (markBatchTerminal, cancellationToken) =>
            {
                await renewalCallFailed.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                markBatchTerminal();
                return 1;
            },
            CancellationToken.None);
        await scopeDisposal.Entered.WaitAsync(TimeSpan.FromSeconds(3));
        scopeDisposal.Release();

        var exception =
            await Assert.ThrowsExactlyAsync<OutboxLeaseLostException>(
                () => processingTask.WaitAsync(TimeSpan.FromSeconds(3)));

        Assert.Contains(
            message.LockId.ToString("D"),
            exception.Message,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ProcessBatchWithLeaseRenewalAsync_WhenRenewalCallPrecedesProcessingFailurePropagatesLeaseFailure()
    {
        var options = new OutboxWorkerOptions
        {
            LeaseSeconds = 6,
            LeaseRenewalSeconds = 1,
        };
        var message = CreateMessage(attempts: 1);
        var store = CreateStore(message);
        var renewalCallFailed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        store
            .RenewLeaseAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                message.LockId,
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(
                _ =>
                {
                    renewalCallFailed.TrySetResult();
                    return Task.FromException(
                        new OutboxLeaseLostException(message.LockId));
                });
        var scopeDisposal = new BlockingAsyncScopeDisposal();
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        await using var provider =
            CreateRenewalScopeProvider(store, scopeDisposal);
        var processor = CreateProcessor(provider, now, options);

        var processingTask = processor.ProcessBatchWithLeaseRenewalAsync(
            [message],
            async (_, cancellationToken) =>
            {
                await renewalCallFailed.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                throw new InvalidOperationException(
                    "Processing failed after lease loss.");
            },
            CancellationToken.None);
        await scopeDisposal.Entered.WaitAsync(TimeSpan.FromSeconds(3));
        scopeDisposal.Release();

        var exception =
            await Assert.ThrowsExactlyAsync<OutboxLeaseLostException>(
                () => processingTask.WaitAsync(TimeSpan.FromSeconds(3)));

        Assert.Contains(
            message.LockId.ToString("D"),
            exception.Message,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ProcessBatchWithLeaseRenewalAsync_WhenTerminalPrecedesZeroRowRenewalReturnsSuccess()
    {
        var options = new OutboxWorkerOptions
        {
            LeaseSeconds = 6,
            LeaseRenewalSeconds = 1,
        };
        var message = CreateMessage(attempts: 1);
        var store = CreateStore(message);
        var releaseProcessing = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        store
            .RenewLeaseAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                message.LockId,
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(
                _ =>
                {
                    releaseProcessing.TrySetResult();
                    return Task.FromException(
                        new OutboxLeaseLostException(message.LockId));
                });
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store);
        var processor = CreateProcessor(provider, now, options);

        var processed = await processor
            .ProcessBatchWithLeaseRenewalAsync(
                [message],
                async (markBatchTerminal, cancellationToken) =>
                {
                    markBatchTerminal();
                    await releaseProcessing.Task
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                    return 1;
                },
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(3));

        Assert.AreEqual(1, processed);
    }

    [TestMethod]
    public async Task ProcessOnceAsync_WhenProcessingFailsBeforeRenewalCleanupPreservesProcessingFailure()
    {
        var completionOrder = new OutboxLeaseCompletionOrder();
        completionOrder.MarkProcessingCompleted();
        completionOrder.MarkRenewalFailed();
        Assert.IsTrue(
            completionOrder.ShouldPreserveProcessingOutcome(
                processingSucceeded: false),
            "处理失败先发生时，稍后的续租清理故障不能覆盖原始异常。");

        var options = new OutboxWorkerOptions
        {
            LeaseSeconds = 6,
            LeaseRenewalSeconds = 1,
        };
        var message = CreateMessage(attempts: 1);
        var store = CreateStore(message);
        var renewalEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRenewal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        store
            .RenewLeaseAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                message.LockId,
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(
                async _ =>
                {
                    renewalEntered.TrySetResult();
                    await releaseRenewal.Task.ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMilliseconds(100))
                        .ConfigureAwait(false);
                    throw new InvalidOperationException(
                        "Renewal cleanup failed.");
                });
        store
            .MarkProcessedAsync(
                message.Id,
                message.LockId,
                Arg.Any<CancellationToken>())
            .Returns(
                async _ =>
                {
                    await renewalEntered.Task
                        .WaitAsync(TimeSpan.FromSeconds(2))
                        .ConfigureAwait(false);
                    releaseRenewal.TrySetResult();
                    throw new InvalidOperationException(
                        "Mark processed failed.");
                });
        store
            .MarkFailedAsync(
                message.Id,
                message.LockId,
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(
                new InvalidOperationException(
                    "Processing state failed.")));
        var handler = new RecordingHandler(
            message.MessageType,
            message.SchemaVersion);
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        await using var provider = CreateProvider(store, handler);
        var processor = CreateProcessor(provider, now, options);

        var exception =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => processor
                    .ProcessOnceAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(4)));

        Assert.AreEqual("Processing state failed.", exception.Message);
    }

    [TestMethod]
    public void GetDelayAfterBatch_OnlyWaitsWhenBatchIsNotFull()
    {
        var options = new OutboxWorkerOptions
        {
            BatchSize = 7,
            PollMilliseconds = 250,
        };
        var store = CreateStore();
        using var provider = CreateProvider(store);
        var processor = CreateProcessor(
            provider,
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
            options);

        Assert.AreEqual(TimeSpan.Zero, processor.GetDelayAfterBatch(7));
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(250),
            processor.GetDelayAfterBatch(6));
    }

    [TestMethod]
    public async Task ProcessOnceAsync_RecordsOperationalBacklogCategoriesAndAges()
    {
        var now = new DateTimeOffset(2026, 7, 26, 0, 2, 0, TimeSpan.Zero);
        var store = CreateStore();
        BacklogReader(store)
            .ReadBacklogAsync(Arg.Any<CancellationToken>())
            .Returns(new OutboxBacklogSnapshot(2, now.AddSeconds(-90))
            {
                DueRetryCount = 3,
                ActiveLeaseCount = 4,
                DeadLetterCount = 5,
                OldestDeadLetteredAtUtc = now.AddSeconds(-120),
            });
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
        CollectionAssert.Contains(
            measurements,
            ("fullnet.outbox.retry.due", 3d));
        CollectionAssert.Contains(
            measurements,
            ("fullnet.outbox.lease.active", 4d));
        CollectionAssert.Contains(
            measurements,
            ("fullnet.outbox.dead_letter.messages", 5d));
        CollectionAssert.Contains(
            measurements,
            ("fullnet.outbox.dead_letter.oldest_age", 120d));
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
    public void OutboxWorkerOptionsValidator_RejectsUnsafeBounds()
    {
        var options = new OutboxWorkerOptions
        {
            BacklogSampleSeconds = 4,
            BatchSize = 2,
            MaxConcurrency = 3,
            LeaseSeconds = 10,
            LeaseRenewalSeconds = 6,
        };
        var validator = new OutboxWorkerOptionsValidator();

        var result = validator.Validate(Options.DefaultName, options);

        Assert.AreEqual(1, new OutboxWorkerOptions().MaxConcurrency);
        Assert.AreEqual(10, new OutboxWorkerOptions().LeaseRenewalSeconds);
        Assert.IsFalse(result.Succeeded);
        CollectionAssert.Contains(
            (result.Failures ?? []).ToArray(),
            "OutboxWorker:BacklogSampleSeconds must be between 5 and 3600.");
        CollectionAssert.Contains(
            (result.Failures ?? []).ToArray(),
            "OutboxWorker:MaxConcurrency must not exceed BatchSize.");
        CollectionAssert.Contains(
            (result.Failures ?? []).ToArray(),
            "OutboxWorker:LeaseRenewalSeconds must not exceed half of LeaseSeconds.");

        var invalidRange = validator.Validate(
            Options.DefaultName,
            new OutboxWorkerOptions
            {
                MaxConcurrency = 17,
                LeaseRenewalSeconds = 0,
            });
        CollectionAssert.Contains(
            (invalidRange.Failures ?? []).ToArray(),
            "OutboxWorker:MaxConcurrency must be between 1 and 16.");
        CollectionAssert.Contains(
            (invalidRange.Failures ?? []).ToArray(),
            "OutboxWorker:LeaseRenewalSeconds must be between 1 and 1200.");
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

    private static ServiceProvider CreateScopedStoreProvider(
        IOutboxStore store,
        ConcurrentQueue<Guid> storeScopeIds,
        params IIntegrationEventHandler[] handlers)
    {
        var services = new ServiceCollection();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<StoreScopeIdentity>();
        services.AddScoped<IOutboxStore>(
            serviceProvider =>
            {
                storeScopeIds.Enqueue(
                    serviceProvider
                        .GetRequiredService<StoreScopeIdentity>()
                        .Id);
                return store;
            });
        services.AddScoped<IOutboxBacklogReader>(
            serviceProvider =>
                (IOutboxBacklogReader)serviceProvider
                    .GetRequiredService<IOutboxStore>());
        foreach (var handler in handlers)
        {
            services.AddSingleton(handler);
        }

        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateRenewalScopeProvider(
        IOutboxStore store,
        BlockingAsyncScopeDisposal scopeDisposal)
    {
        var services = new ServiceCollection();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped(_ => scopeDisposal);
        services.AddScoped<IOutboxStore>(
            serviceProvider =>
            {
                _ = serviceProvider
                    .GetRequiredService<BlockingAsyncScopeDisposal>();
                return store;
            });
        return services.BuildServiceProvider();
    }

    private static IOutboxBacklogReader BacklogReader(IOutboxStore store) =>
        (IOutboxBacklogReader)store;

    private static bool ContainsOnly(
        IReadOnlyCollection<Guid>? messageIds,
        Guid expectedId) =>
        messageIds is not null
        && messageIds.Count == 1
        && messageIds.Contains(expectedId);

    private static OutboxEnvelope CreateMessage(
        int attempts,
        string messageType = "fullnet.test.event",
        int schemaVersion = 1,
        string contentType = "application/x-msgpack",
        Guid? lockId = null) => new(
        Guid.CreateVersion7(),
        lockId ?? Guid.CreateVersion7(),
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

        public IntegrationEventContext? LastContext { get; private set; }

        public ReadOnlyMemory<byte> LastPayload { get; private set; }

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            LastContext = context;
            return HandleAsync(payload, cancellationToken);
        }

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

    private sealed class BlockingHandler(string eventType, int schemaVersion)
        : IIntegrationEventHandler
    {
        private readonly TaskCompletionSource _canceled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string EventType => eventType;

        public int SchemaVersion => schemaVersion;

        public Task Canceled => _canceled.Task;

        public Task Entered => _entered.Task;

        public async Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _canceled.TrySetResult();
                throw;
            }
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class BlockingAsyncScopeDisposal : IAsyncDisposable
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public async ValueTask DisposeAsync()
        {
            _entered.TrySetResult();
            await _release.Task.ConfigureAwait(false);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class StoreScopeIdentity
    {
        public Guid Id { get; } = Guid.CreateVersion7();
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
