using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modularity.Messaging;
using Full.NET.Serialization.MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 创建 Scope B 的生产 Inbox/Dispatcher/Handler 依赖图和真实 Kafka Driver。
/// </summary>
public sealed class KafkaWorkerScenarioDriverFactory
    : IKafkaCapacityScenarioDriverFactory
{
    public string ScopeCode => KafkaCapacityScopeCodes.WorkerInboxHandler;

    public KafkaCapacityDriverRuntime Create(KafkaCapacityConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var observer = new KafkaCapacityWorkerObserver(100_000_000);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<KafkaMessagingOptions>()
            .Configure(options => CopyKafkaOptions(configuration.Kafka, options));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetMessagePack();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());

        var values = new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Provider"] =
                configuration.Database.Provider.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] =
                configuration.Database.ConnectionString,
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] =
                configuration.Database.CommandTimeoutSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                configuration.Database.MySqlGuidStorageMode.ToString(),
        };
        var databaseConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        services.AddFullNetDapper(databaseConfiguration, "Capacity");
        services.AddFullNetModularity();
        services.AddSingleton(observer);
        services.AddScoped<IIntegrationEventSubscription, KafkaCapacityWorkerSubscription>();
        services.AddScoped(_ => IntegrationEventTopicDefinition.Create(
            KafkaCapacityWorkerContracts.TopicCode,
            KafkaCapacityWorkerContracts.EventType,
            KafkaCapacityWorkerContracts.SchemaVersion,
            EventDeliveryOwner.CdcKafka));
        services.RemoveAll<IIntegrationEventSubscriptionCatalog>();
        services.RemoveAll<IntegrationEventSubscriptionCatalog>();
        services.AddScoped<IIntegrationEventSubscriptionCatalog>(provider =>
            new IntegrationEventSubscriptionCatalog(
                provider.GetServices<IntegrationEventTopicDefinition>(),
                provider.GetServices<IIntegrationEventSubscription>()));
        services.AddScoped(provider =>
            (IntegrationEventSubscriptionCatalog)provider
                .GetRequiredService<IIntegrationEventSubscriptionCatalog>());
        services.AddSingleton<KafkaEnvelopeReader>();
        services.AddSingleton<KafkaOffsetCommitter>();
        services.AddSingleton<KafkaFailureClassifier>();
        services.AddSingleton<KafkaMessagingProducer>();
        services.AddSingleton<KafkaRetryRouter>();
        services.AddSingleton<KafkaDeadLetterPublisher>();
        services.AddSingleton<KafkaConsumerMessageProcessor>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var executor = new KafkaCapacityWorkerExecutor(
            configuration.Kafka,
            provider,
            observer);
        return new KafkaCapacityDriverRuntime(
            new KafkaWorkerScenarioDriver(executor),
            executor,
            new KafkaCapacityDatabasePreflight(configuration.Database));
    }

    private static void CopyKafkaOptions(
        KafkaMessagingOptions source,
        KafkaMessagingOptions destination)
    {
        foreach (var property in typeof(KafkaMessagingOptions).GetProperties()
                     .Where(static property => property.CanRead && property.CanWrite))
        {
            property.SetValue(destination, property.GetValue(source));
        }
    }
}

public sealed class KafkaWorkerScenarioDriver(
    KafkaCapacityWorkerExecutor executor) : IKafkaCapacityScenarioDriver,
    IAsyncDisposable
{
    public string ScopeCode => KafkaCapacityScopeCodes.WorkerInboxHandler;

    public async Task<KafkaCapacitySampleEvidence> ExecuteAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(context.Sample.ScopeCode, ScopeCode, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Scope B driver received another scope.");
        }

        return await executor.ExecuteAsync(context, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => executor.DisposeAsync();
}

/// <summary>
/// 使用生产分区 Scheduler、连续 Offset 水位和单消息处理器执行 Scope B 样本。
/// </summary>
public sealed class KafkaCapacityWorkerExecutor(
    KafkaMessagingOptions options,
    ServiceProvider serviceProvider,
    KafkaCapacityWorkerObserver observer)
    : IKafkaCapacityStatisticsSource, IAsyncDisposable
{
    private readonly ConcurrentQueue<KafkaCapacityLibrdkafkaStatisticsEvidence>
        statistics = new();

    public IReadOnlyList<KafkaCapacityLibrdkafkaStatisticsEvidence> SnapshotStatistics() =>
        statistics.ToArray();

    public async Task<KafkaCapacitySampleEvidence> ExecuteAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken)
    {
        if (context.Warmup > TimeSpan.Zero)
        {
            var warmupEvidence = await ExecuteAsync(
                    context.CreateWarmupPhase(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!warmupEvidence.Integrity.CorrectnessPassed
                || warmupEvidence.State != KafkaCapacitySampleState.Completed)
            {
                return warmupEvidence with
                {
                    State = KafkaCapacitySampleState.Incomplete,
                    FailureCodes = warmupEvidence.FailureCodes
                        .Append("warmup_failed")
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                };
            }
        }

        observer.BeginPhase(context.RunHash, context.SampleHash);
        var tracker = new KafkaCapacityIntegrityTracker(
            context.MaximumMessages,
            context.TopicIdentity.Partitions);
        var plan = new WorkerRoutePlan();
        var processor = serviceProvider.GetRequiredService<KafkaConsumerMessageProcessor>();
        using var consumer = new ConsumerBuilder<string, byte[]>(
                BuildConsumerConfig(context))
            .Build();
        await using var scheduler = new KafkaPartitionWorkScheduler(
            (result, token) => processor.ProcessScheduledMessageAsync(plan, result, token),
            options);
        var coordinator = new KafkaConsumerPartitionCoordinator(
            consumer,
            scheduler,
            options,
            KafkaCapacityWorkerContracts.ConsumerName,
            serviceProvider.GetRequiredService<ILogger<KafkaCapacityWorkerExecutor>>());
        var assignments = Enumerable.Range(0, context.TopicIdentity.Partitions)
            .Select(partition => new TopicPartitionOffset(
                context.TopicIdentity.TopicName,
                new Partition(partition),
                QueryHighWatermark(consumer, context.TopicIdentity.TopicName, partition)))
            .ToArray();
        consumer.Assign(assignments);
        coordinator.OnAssigned(assignments.Select(static item => item.TopicPartition));

        using var producer = new ProducerBuilder<string, byte[]>(
                BuildProducerConfig(context))
            .Build();
        var partitionSequences = new long[context.TopicIdentity.Partitions];
        var partitionProduceLocks = Enumerable.Range(
                0,
                context.TopicIdentity.Partitions)
            .Select(static _ => new object())
            .ToArray();
        var scheduleLatency = new KafkaCapacityLatencyHistogram();
        var acknowledgementLatency = new KafkaCapacityLatencyHistogram();
        var failureCodes = new ConcurrentDictionary<string, byte>(
            StringComparer.Ordinal);
        var before = WorkerResourceSnapshot.Capture();
        var stopwatch = Stopwatch.StartNew();
        var schedulingTask = new KafkaCapacityOpenLoopScheduler().RunAsync(
            context.Sample.TargetMessagesPerSecond,
            context.Duration,
            context.MaximumMessages,
            context.Sample.ProducerConcurrency,
            (scheduledMessage, token) =>
            {
                token.ThrowIfCancellationRequested();
                var sequence = scheduledMessage.GlobalSequence;
                var partition = checked((int)(sequence
                    % context.TopicIdentity.Partitions));
                var observedEnqueued = Stopwatch.GetElapsedTime(
                    0,
                    Stopwatch.GetTimestamp()).Ticks / 10;
                var enqueued = Math.Max(
                    scheduledMessage.ScheduledTimestampMicroseconds,
                    observedEnqueued);
                if (!scheduleLatency.RecordMicroseconds(Math.Max(
                        1,
                        enqueued - scheduledMessage.ScheduledTimestampMicroseconds)))
                {
                    failureCodes.TryAdd("schedule_latency_histogram_overflow", 0);
                }

                lock (partitionProduceLocks[partition])
                {
                    var partitionSequence = partitionSequences[partition]++;
                    var value = KafkaCapacityEnvelopeCodec.Encode(
                        context.Sample.PayloadSizeBytes,
                        context.RunHash,
                        context.SampleHash,
                        sequence,
                        partitionSequence,
                        scheduledMessage.ScheduledTimestampMicroseconds,
                        enqueued);
                    tracker.OnEnqueued(sequence);
                    producer.Produce(
                        new TopicPartition(
                            context.TopicIdentity.TopicName,
                            new Partition(partition)),
                        BuildMessage(sequence, partition, value),
                        report =>
                        {
                            if (report.Error.IsError
                                || report.Status != PersistenceStatus.Persisted)
                            {
                                failureCodes.TryAdd("delivery_not_persisted", 0);
                                return;
                            }

                            try
                            {
                                tracker.OnAcknowledged(sequence);
                                var acknowledged = Stopwatch.GetElapsedTime(
                                    0,
                                    Stopwatch.GetTimestamp()).Ticks / 10;
                                if (!acknowledgementLatency.RecordMicroseconds(
                                        Math.Max(1, acknowledged - enqueued)))
                                {
                                    failureCodes.TryAdd(
                                        "acknowledgement_latency_histogram_overflow",
                                        0);
                                }
                            }
                            catch
                            {
                                failureCodes.TryAdd("delivery_tracking_failed", 0);
                            }
                        });
                }
                return ValueTask.CompletedTask;
            },
            cancellationToken);
        while (!schedulingTask.IsCompleted)
        {
            PollAvailable(consumer, coordinator, tracker, context);
            await Task.Yield();
        }

        var scheduling = await schedulingTask.ConfigureAwait(false);

        if (producer.Flush(context.DrainTimeout) != 0)
        {
            failureCodes.TryAdd("producer_flush_incomplete", 0);
        }
        var drainStarted = Stopwatch.StartNew();
        while (drainStarted.Elapsed < context.DrainTimeout)
        {
            PollAvailable(consumer, coordinator, tracker, context);
            coordinator.ProcessCompletions(DateTimeOffset.UtcNow);
            if (observer.Snapshot().Processed >= tracker.Complete(false).Acknowledged
                && scheduler.InFlightCount == 0)
            {
                break;
            }

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }

        await scheduler.StopAsync(context.DrainTimeout).ConfigureAwait(false);
        coordinator.ProcessCompletions(DateTimeOffset.UtcNow);
        coordinator.OnRevoked(consumer.Assignment);
        consumer.Close();
        stopwatch.Stop();
        var after = WorkerResourceSnapshot.Capture();

        var handler = observer.Snapshot();
        var baseIntegrity = tracker.Complete(
            handler.Processed == tracker.Complete(false).Acknowledged
            && scheduler.InFlightCount == 0);
        var integrity = baseIntegrity with
        {
            Consumed = handler.Processed,
            Corrupted = baseIntegrity.Corrupted + handler.Corrupted,
        };
        var elapsedSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.000001);
        var failures = failureCodes.Keys
            .Order(StringComparer.Ordinal)
            .ToArray();
        var completed = integrity.CorrectnessPassed && failures.Length == 0;
        return new KafkaCapacitySampleEvidence(
            context.Sample.ScopeCode,
            context.Sample.SampleId,
            context.Sample.Scenario,
            context.Sample.TargetMessagesPerSecond,
            context.Sample.PayloadSizeBytes,
            context.TopicIdentity.Partitions,
            context.Sample.ProducerConcurrency,
            completed
                ? KafkaCapacitySampleState.Completed
                : KafkaCapacitySampleState.Incomplete,
            integrity,
            new KafkaCapacityPerformanceEvidence(
                scheduling.Scheduled / elapsedSeconds,
                integrity.Acknowledged / elapsedSeconds,
                handler.Processed / elapsedSeconds,
                scheduleLatency.Snapshot(),
                acknowledgementLatency.Snapshot(),
                handler.EndToEndLatency,
                (long)drainStarted.Elapsed.TotalMilliseconds,
                after.CpuTime <= before.CpuTime || stopwatch.Elapsed <= TimeSpan.Zero
                    ? 0
                    : (after.CpuTime - before.CpuTime).TotalMilliseconds
                        / stopwatch.Elapsed.TotalMilliseconds
                        / Math.Max(1, Environment.ProcessorCount)
                        * 100d,
                after.ManagedHeapBytes,
                scheduler.BufferDepth,
                after.AllocatedBytes - before.AllocatedBytes,
                after.WorkingSetBytes,
                after.Gen0Collections - before.Gen0Collections,
                after.Gen1Collections - before.Gen1Collections,
                after.Gen2Collections - before.Gen2Collections),
            completed
                ? []
                : failures.Append("scope_b_correctness_failed")
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
    }

    public ValueTask DisposeAsync()
    {
        serviceProvider.Dispose();
        return ValueTask.CompletedTask;
    }

    private ConsumerConfig BuildConsumerConfig(KafkaCapacitySampleContext context)
    {
        var config = options.BuildConsumerConfig(context.ConsumerGroupId);
        config.ClientId = context.ConsumerClientId;
        config.AutoOffsetReset = AutoOffsetReset.Latest;
        return config;
    }

    private ProducerConfig BuildProducerConfig(KafkaCapacitySampleContext context)
    {
        var config = options.BuildProducerConfig();
        config.ClientId = context.ProducerClientId;
        return config;
    }

    private Offset QueryHighWatermark(
        IConsumer<string, byte[]> consumer,
        string topic,
        int partition) =>
        consumer.QueryWatermarkOffsets(
            new TopicPartition(topic, new Partition(partition)),
            TimeSpan.FromMilliseconds(options.DeliveryTimeoutMilliseconds)).High;

    private static Message<string, byte[]> BuildMessage(
        long sequence,
        int partition,
        byte[] value)
    {
        var eventId = Guid.CreateVersion7();
        var headers = new Headers
        {
            { KafkaEnvelopeHeaderNames.EventId, Encoding.UTF8.GetBytes(eventId.ToString("D")) },
            { KafkaEnvelopeHeaderNames.MessageType, Encoding.UTF8.GetBytes(KafkaCapacityWorkerContracts.EventType) },
            { KafkaEnvelopeHeaderNames.SchemaVersion, Encoding.UTF8.GetBytes("1") },
            { KafkaEnvelopeHeaderNames.ContentType, Encoding.UTF8.GetBytes(MessagingNames.ContentTypeMessagePack) },
            { KafkaEnvelopeHeaderNames.Producer, Encoding.UTF8.GetBytes("fullnet.capacity.runner") },
            { KafkaEnvelopeHeaderNames.OccurredAtUtc, Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O")) },
        };
        return new Message<string, byte[]>
        {
            Key = $"capacity-{partition}-{sequence % 64}",
            Value = value,
            Headers = headers,
        };
    }

    private static void PollAvailable(
        IConsumer<string, byte[]> consumer,
        KafkaConsumerPartitionCoordinator coordinator,
        KafkaCapacityIntegrityTracker tracker,
        KafkaCapacitySampleContext context)
    {
        coordinator.ProcessCompletions(DateTimeOffset.UtcNow);
        coordinator.ResumeDuePartitions(DateTimeOffset.UtcNow);
        var result = consumer.Consume(TimeSpan.Zero);
        if (result?.Message is null || result.IsPartitionEOF)
        {
            return;
        }

        if (!KafkaCapacityEnvelopeCodec.TryDecode(result.Message.Value, out var envelope))
        {
            tracker.OnCorrupted();
        }
        else
        {
            tracker.OnConsumed(
                envelope.GlobalSequence,
                result.Partition.Value,
                envelope.PartitionSequence,
                envelope.RunHash == context.RunHash
                && envelope.SampleHash == context.SampleHash);
        }

        if (!coordinator.TryDispatch(result))
        {
            throw new InvalidOperationException(
                "Scope B production scheduler rejected a polled Kafka record.");
        }
    }

    private sealed class WorkerRoutePlan : IKafkaConsumerRoutePlan
    {
        private int revoked;

        public string ConsumerName => KafkaCapacityWorkerContracts.ConsumerName;

        public bool HasOwnershipRevoked => Volatile.Read(ref revoked) != 0;

        public bool ContainsRoute(string eventType, int schemaVersion) =>
            string.Equals(
                eventType,
                KafkaCapacityWorkerContracts.EventType,
                StringComparison.Ordinal)
            && schemaVersion == KafkaCapacityWorkerContracts.SchemaVersion;

        public void SetOwnershipRevoked(
            string eventType,
            int schemaVersion,
            bool isRevoked) =>
            Volatile.Write(ref revoked, isRevoked ? 1 : 0);

        public string ResolveTopicCode(string topic) =>
            KafkaCapacityWorkerContracts.TopicCode;
    }

    private sealed record WorkerResourceSnapshot(
        TimeSpan CpuTime,
        long ManagedHeapBytes,
        long AllocatedBytes,
        long WorkingSetBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections)
    {
        public static WorkerResourceSnapshot Capture()
        {
            using var process = Process.GetCurrentProcess();
            return new WorkerResourceSnapshot(
                process.TotalProcessorTime,
                GC.GetTotalMemory(false),
                GC.GetTotalAllocatedBytes(precise: false),
                process.WorkingSet64,
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2));
        }
    }
}
