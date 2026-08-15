using System.Diagnostics;
using System.Diagnostics.Metrics;
using Full.NET.Abstractions.Messaging;
using Full.NET.Host.Worker;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class MessagingWorkerOptionsTests
{
    [TestMethod]
    public void Kafka_consumer_worker_does_not_capture_scoped_message_services()
    {
        var constructorParameters = typeof(KafkaConsumerWorker)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.DoesNotContain(
            constructorParameters,
            typeof(Full.NET.Modularity.Messaging.IntegrationEventConsumerDispatcher));
        CollectionAssert.DoesNotContain(
            constructorParameters,
            typeof(IntegrationEventSubscriptionCatalog));
        CollectionAssert.DoesNotContain(
            constructorParameters,
            typeof(IEnumerable<IIntegrationEventSubscription>));
        CollectionAssert.Contains(constructorParameters, typeof(IServiceScopeFactory));
        CollectionAssert.Contains(
            constructorParameters,
            typeof(KafkaConsumerMessageProcessor));
        CollectionAssert.DoesNotContain(
            constructorParameters,
            typeof(KafkaEnvelopeReader));
    }

  private const string EventType = "fullnet.tenancy.tenant.changed";

    [TestMethod]
    public void MessagingWorkerOptions_default_mode_is_legacy_polling()
    {
        using var provider = CreateProvider(new Dictionary<string, string?>());
        var options = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<MessagingWorkerOptions>>()
            .Value;

        Assert.AreEqual(MessagingWorkerMode.LegacyPolling, options.Mode);
    }

    [TestMethod]
    public void MessagingWorkerOptions_rejects_legacy_polling_with_kafka_enabled()
    {
        using var provider = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Messaging:Worker:Mode"] = "LegacyPolling",
                ["Messaging:Kafka:Enabled"] = "true",
            });

        var exception = Assert.ThrowsExactly<
            Microsoft.Extensions.Options.OptionsValidationException>(
            provider.GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>().Validate);

        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Messaging:Worker:Mode LegacyPolling cannot be combined with Messaging:Kafka:Enabled=true.");
    }

    [TestMethod]
    public void MessagingWorkerOptions_rejects_shadow_mode_without_shadow_comparison()
    {
        using var provider = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Messaging:Worker:Mode"] = "ShadowCdc",
                ["Messaging:ShadowComparison:Enabled"] = "false",
            });

        var exception = Assert.ThrowsExactly<
            Microsoft.Extensions.Options.OptionsValidationException>(
            provider.GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>().Validate);

        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Messaging:Worker:Mode ShadowCdc requires Messaging:ShadowComparison:Enabled=true.");
    }

    [TestMethod]
    public void MessagingWorkerOptions_rejects_cdc_kafka_without_kafka_enabled()
    {
        // 旧配置字符串 CdcKafka 仍可反序列化并通过验证器（因被规范化为 HybridKafka），
        // 但 Kafka 关闭时应拒绝，错误消息按 HybridKafka（有效语义）输出。
        using var provider = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Messaging:Worker:Mode"] = "CdcKafka",
                ["Messaging:Kafka:Enabled"] = "false",
            });

        var exception = Assert.ThrowsExactly<
            Microsoft.Extensions.Options.OptionsValidationException>(
            provider.GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>().Validate);

        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Messaging:Worker:Mode HybridKafka requires Messaging:Kafka:Enabled=true.");
    }

    [TestMethod]
    public void MessagingWorkerOptions_rejects_simultaneous_shadow_and_kafka_formal_paths()
    {
        using var provider = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Messaging:Worker:Mode"] = "CdcKafka",
                ["Messaging:Kafka:Enabled"] = "true",
                ["Messaging:ShadowComparison:Enabled"] = "true",
            });

        var exception = Assert.ThrowsExactly<
            Microsoft.Extensions.Options.OptionsValidationException>(
            provider.GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>().Validate);

        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Messaging:Worker:Mode HybridKafka cannot be combined with Messaging:ShadowComparison:Enabled=true.");
    }

    [TestMethod]
    public void MessagingWorkerCatalogGuard_rejects_business_subscriptions_in_shadow_mode()
    {
        var topic = IntegrationEventTopicDefinition.Create(
            "tenancy.tenant-changed.kafka.v1",
            EventType,
            1,
            EventDeliveryOwner.CdcKafka);
        var subscription = new TestSubscription(
            "fullnet.tenancy.projector-a",
            EventType,
            1);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            MessagingWorkerCatalogGuard.ValidateShadowMode(
                [subscription],
                [topic]));

        StringAssert.Contains(exception.Message, "ShadowCdc cannot register business subscriptions");
        StringAssert.Contains(exception.Message, EventType);
    }

    [TestMethod]
    public void MessagingWorkerCatalogGuard_rejects_cdc_mode_without_real_subscriptions()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            MessagingWorkerCatalogGuard.ValidateCdcKafkaMode([]));

        StringAssert.Contains(
            exception.Message,
            "CdcKafka delivery mode requires at least one production IIntegrationEventSubscription");
    }

    [TestMethod]
    public void MessagingWorkerOptions_allows_hybrid_kafka_with_kafka_enabled_and_legacy_poller()
    {
        // HybridKafka 模式必须允许 Kafka:Enabled=true，同时 Legacy Poller 仍运行。
        // 当前验证器禁止 LegacyPolling + Kafka，但 HybridKafka 是新的并存模式。
        using var provider = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Messaging:Worker:Mode"] = "HybridKafka",
                ["Messaging:Kafka:Enabled"] = "true",
            });

        // RED 期望：当前 HybridKafka 枚举不存在或验证器拒绝此组合 → 抛异常。
        // GREEN 期望：HybridKafka 同时允许 Kafka 与 Legacy Poller 并存 → 验证通过。
        var startupValidator = provider
            .GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>();
        startupValidator.Validate();

        // 到达此处表示验证通过，Options.Mode 应等于 HybridKafka。
        var options = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<MessagingWorkerOptions>>()
            .Value;
        Assert.AreEqual(MessagingWorkerMode.HybridKafka, options.Mode);
    }

    [TestMethod]
    public void MessagingWorkerCatalogGuard_validate_hybrid_kafka_mode_rejects_cdc_stream_without_subscription()
    {
        // 构造一个 CdcKafka owner 的 Topic，但没有对应订阅 → 应抛异常（按流校验）。
        var topicWithCdcOwner = IntegrationEventTopicDefinition.Create(
            "organization.unit-changed.kafka.v1",
            "fullnet.organization.unit.changed",
            1,
            EventDeliveryOwner.CdcKafka);
        var unrelatedLegacyTopic = IntegrationEventTopicDefinition.Create(
            "tenancy.tenant-changed.legacy.v1",
            "fullnet.tenancy.tenant.changed",
            1,
            EventDeliveryOwner.LegacyPolling);
        var unrelatedSubscription = new TestSubscription(
            "fullnet.tenancy.projector-a",
            "fullnet.tenancy.tenant.changed",
            1);

        // RED 期望：ValidateHybridKafkaMode 方法不存在或只校验全局数量（有任意订阅就通过）。
        // GREEN 期望：CdcKafka 流 fullnet.organization.unit.changed 没有对应订阅 → 按流失败。
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            MessagingWorkerCatalogGuard.ValidateHybridKafkaMode(
                [unrelatedSubscription],
                [topicWithCdcOwner, unrelatedLegacyTopic]));

        StringAssert.Contains(exception.Message, "fullnet.organization.unit.changed");
        StringAssert.Contains(exception.Message, "subscription");
    }

    [TestMethod]
    public void MessagingWorkerCatalogGuard_validate_hybrid_kafka_mode_accepts_cdc_streams_with_matching_subscriptions()
    {
        // 每个 CdcKafka owner 的 Topic 都有对应订阅 → 应通过。
        var topicWithCdcOwner = IntegrationEventTopicDefinition.Create(
            "organization.unit-changed.kafka.v1",
            "fullnet.organization.unit.changed",
            1,
            EventDeliveryOwner.CdcKafka);
        var legacyTopic = IntegrationEventTopicDefinition.Create(
            "tenancy.tenant-changed.legacy.v1",
            "fullnet.tenancy.tenant.changed",
            1,
            EventDeliveryOwner.LegacyPolling);
        var matchingSubscription = new TestSubscription(
            "fullnet.identity.organization-unit-projection",
            "fullnet.organization.unit.changed",
            1);

        // 不应抛异常。
        MessagingWorkerCatalogGuard.ValidateHybridKafkaMode(
            [matchingSubscription],
            [topicWithCdcOwner, legacyTopic]);
    }

    [TestMethod]
    public void MessagingWorkerCatalogGuard_validate_hybrid_kafka_mode_accepts_all_legacy_topics_without_kafka_subscriptions()
    {
        // 默认情况所有流都是 Legacy owner → Hybrid 模式可以启动，Kafka 不产生副作用。
        var allLegacyTopic = IntegrationEventTopicDefinition.Create(
            "tenancy.tenant-changed.legacy.v1",
            "fullnet.tenancy.tenant.changed",
            1,
            EventDeliveryOwner.LegacyPolling);

        // 零订阅也应允许，因为没有 CdcKafka 流需要消费。
        MessagingWorkerCatalogGuard.ValidateHybridKafkaMode(
            [],
            [allLegacyTopic]);
    }

    [TestMethod]
    public void KafkaMessagingTelemetry_uses_low_cardinality_tag_names_only()
    {
        var allowedTags = new[]
        {
            "provider",
            "topic_code",
            "consumer_code",
            "message_type_code",
            "result",
            "reason_code",
        };
        var observedTags = new List<string>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == KafkaMessagingTelemetry.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
            {
                observedTags.Add(tag.Key);
            }
        });
        listener.Start();

        KafkaMessagingTelemetry.RecordConsume(
            "kafka",
            "tenancy.tenant-changed.v1",
            "fullnet.tenancy.projector-a",
            EventType,
            "success",
            "messaging.transient.timeout");

        listener.RecordObservableInstruments();

        Assert.IsTrue(observedTags.Count > 0);
        foreach (var tag in observedTags)
        {
            CollectionAssert.Contains(allowedTags, tag);
        }
    }

    [TestMethod]
    public void KafkaMessagingTelemetry_exposes_bounded_consumer_state_gauges()
    {
        var measurements = new Dictionary<string, long>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == KafkaMessagingTelemetry.MeterName
                && instrument.Name.StartsWith("fullnet.messaging.kafka.", StringComparison.Ordinal))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "consumer_code"
                    && string.Equals(
                        tag.Value as string,
                        "fullnet.messaging.orders",
                        StringComparison.Ordinal))
                {
                    measurements[instrument.Name] = measurement;
                }
            }
        });
        listener.Start();

        KafkaMessagingTelemetry.UpdateConsumerState(
            "kafka",
            "fullnet.messaging.orders",
            inflight: 7,
            bufferDepth: 11,
            assignedPartitions: 4,
            pausedPartitions: 2,
            ownershipRevoked: true);
        KafkaMessagingTelemetry.UpdateProcessingState(
            "kafka",
            "fullnet.messaging.orders",
            sequence: 2,
            inflight: 3,
            bufferDepth: 4);
        KafkaMessagingTelemetry.UpdateProcessingState(
            "kafka",
            "fullnet.messaging.orders",
            sequence: 1,
            inflight: 99,
            bufferDepth: 99);
        listener.RecordObservableInstruments();
        KafkaMessagingTelemetry.RemoveConsumerState("fullnet.messaging.orders");

        Assert.AreEqual(3L, measurements["fullnet.messaging.kafka.inflight"]);
        Assert.AreEqual(4L, measurements["fullnet.messaging.kafka.buffer.depth"]);
        Assert.AreEqual(4L, measurements["fullnet.messaging.kafka.assigned.partitions"]);
        Assert.AreEqual(2L, measurements["fullnet.messaging.kafka.paused.partitions"]);
        Assert.AreEqual(1L, measurements["fullnet.messaging.kafka.ownership.revoked"]);
    }

    [TestMethod]
    public void KafkaMessagingTelemetry_creates_consume_and_commit_activities()
    {
        var stopped = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == KafkaMessagingTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped.Add(activity.OperationName),
        };
        ActivitySource.AddActivityListener(listener);

        using (KafkaMessagingTelemetry.StartConsumeActivity(
                   "messaging.orders.v1",
                   "fullnet.messaging.orders",
                   2,
                   42,
                   "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"))
        {
        }

        using (KafkaMessagingTelemetry.StartCommitActivity(
                   "fullnet.messaging.orders",
                   2))
        {
        }

        CollectionAssert.Contains(stopped, "fullnet.messaging.kafka.consume");
        CollectionAssert.Contains(stopped, "fullnet.messaging.kafka.commit");
    }

    [TestMethod]
    public void KafkaMessagingOptions_rejects_plaintext_security_in_production_when_enabled()
    {
        using var provider = CreateKafkaProvider(
            new Dictionary<string, string?>
            {
                ["Messaging:Kafka:Enabled"] = "true",
                ["Messaging:Kafka:BootstrapServers"] = "kafka:9092",
                ["Messaging:Kafka:SecurityProtocol"] = "Plaintext",
            },
            Environments.Production);

        var exception = Assert.ThrowsExactly<
            Microsoft.Extensions.Options.OptionsValidationException>(
            provider.GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>().Validate);

        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Messaging:Kafka:SecurityProtocol must use TLS in Production.");
    }

    [TestMethod]
    public void KafkaMessagingOptions_binds_consumer_protocol_and_bounded_producer_settings()
    {
        using var provider = CreateKafkaProvider(
            new Dictionary<string, string?>
            {
                ["Messaging:Kafka:Enabled"] = "true",
                ["Messaging:Kafka:ConsumerGroupProtocol"] = "Consumer",
                ["Messaging:Kafka:BrokerMajorVersion"] = "4",
                ["Messaging:Kafka:ProducerLingerMilliseconds"] = "7",
                ["Messaging:Kafka:ProducerBatchSizeBytes"] = "131072",
                ["Messaging:Kafka:ProducerQueueMaxMessages"] = "30000",
                ["Messaging:Kafka:ProducerQueueMaxKbytes"] = "131072",
                ["Messaging:Kafka:ProducerMaxInFlightRequests"] = "4",
                ["Messaging:Kafka:ConsumerBufferHighWatermark"] = "512",
                ["Messaging:Kafka:ConsumerBufferLowWatermark"] = "256",
                ["Messaging:Kafka:PartitionBufferHighWatermark"] = "32",
                ["Messaging:Kafka:PartitionBufferLowWatermark"] = "8",
                ["Messaging:Kafka:PartitionKeyConcurrencySlots"] = "8",
                ["Messaging:Kafka:OffsetCommitMode"] = "PeriodicWatermark",
                ["Messaging:Kafka:OffsetCommitIntervalMilliseconds"] = "750",
                ["Messaging:Kafka:OffsetCommitBatchSize"] = "250",
            },
            Environments.Development);

        provider.GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>().Validate();
        var options = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<KafkaMessagingOptions>>().Value;

        Assert.AreEqual(KafkaConsumerGroupProtocolMode.Consumer, options.ConsumerGroupProtocol);
        Assert.AreEqual(4, options.BrokerMajorVersion);
        Assert.AreEqual(7, options.ProducerLingerMilliseconds);
        Assert.AreEqual(131_072, options.ProducerBatchSizeBytes);
        Assert.AreEqual(30_000, options.ProducerQueueMaxMessages);
        Assert.AreEqual(131_072, options.ProducerQueueMaxKbytes);
        Assert.AreEqual(4, options.ProducerMaxInFlightRequests);
        Assert.AreEqual(512, options.ConsumerBufferHighWatermark);
        Assert.AreEqual(256, options.ConsumerBufferLowWatermark);
        Assert.AreEqual(32, options.PartitionBufferHighWatermark);
        Assert.AreEqual(8, options.PartitionBufferLowWatermark);
        Assert.AreEqual(8, options.PartitionKeyConcurrencySlots);
        Assert.AreEqual(KafkaOffsetCommitMode.PeriodicWatermark, options.OffsetCommitMode);
        Assert.AreEqual(750, options.OffsetCommitIntervalMilliseconds);
        Assert.AreEqual(250, options.OffsetCommitBatchSize);
    }

    private static ServiceProvider CreateProvider(
        IReadOnlyDictionary<string, string?> configurationValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Development));
        services.AddOptions<MessagingWorkerOptions>()
            .Bind(configuration.GetSection(MessagingWorkerOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            Microsoft.Extensions.Options.IValidateOptions<MessagingWorkerOptions>,
            MessagingWorkerOptionsValidator>();
        services.AddSingleton<IConfiguration>(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
        });
    }

    private static ServiceProvider CreateKafkaProvider(
        IReadOnlyDictionary<string, string?> configurationValues,
        string environmentName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
        services
            .AddOptions<KafkaMessagingOptions>()
            .Bind(configuration.GetSection(KafkaMessagingOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                Microsoft.Extensions.Options.IValidateOptions<KafkaMessagingOptions>,
                KafkaMessagingOptionsValidator>());
        services.AddSingleton<IConfiguration>(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
        });
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Full.NET.Host.Worker";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
    }

    private sealed class TestSubscription(
        string consumerName,
        string eventType,
        int schemaVersion)
        : IIntegrationEventSubscription
    {
        public string ConsumerName { get; } = consumerName;

        public string EventType { get; } = eventType;

        public int SchemaVersion { get; } = schemaVersion;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
