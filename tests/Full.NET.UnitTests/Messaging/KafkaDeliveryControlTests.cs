using Confluent.Kafka;
using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

/// <summary>覆盖控制面动作顺序、跨进程资源恢复及失败关闭。</summary>
[TestClass]
public sealed class KafkaDeliveryControlTests
{
    [TestMethod]
    public async Task Prepare_waits_for_cdc_before_pause_and_drains_every_group_and_retry_topic()
    {
        await using var fixture = new ControlFixture();
        var positions = 0;
        fixture.Connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns(_ =>
            Task.FromResult<CdcDeliveryPosition?>(CdcDeliveryPosition.ForMySql(null, "mysql-bin.000001", ++positions == 1 ? 1 : 10)));
        fixture.Connect.PauseConnectorAsync("orders", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.IsTrue(positions >= 2, "CDC 尚未覆盖边界时不得暂停。");
            Assert.AreEqual(EventDeliveryRollbackPhase.PauseRequested, fixture.Journal.Operation.Phase);
            Assert.IsNotNull(fixture.Journal.Operation.ResourcesJson);
            fixture.Paused = true;
            return Task.CompletedTask;
        });
        var proof = await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation);
        Assert.IsTrue(proof.ConnectorStopped && proof.BrokerMessagesDrainedOrIsolated && proof.SourcePositionCoversProducerFence);
        CollectionAssert.AreEquivalent(new[]
        {
            "fullnet.messaging.orders.a|messaging.orders.v1", "fullnet.messaging.orders.b|messaging.orders.v1",
            "fullnet.messaging.orders.a|messaging.orders.v1.retry.5s", "fullnet.messaging.orders.b|messaging.orders.v1.retry.5s",
        }, fixture.Queries.Distinct().ToArray());
        Assert.AreEqual(EventDeliveryRollbackPhase.Paused, fixture.Journal.Operation.Phase);
    }

    [TestMethod]
    public async Task Pause_response_loss_is_recoverable_by_a_new_reader_using_persisted_resources()
    {
        await using var fixture = new ControlFixture();
        fixture.Connect.PauseConnectorAsync("orders", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            fixture.Paused = true;
            return Task.FromException(new HttpRequestException("暂停已生效但响应丢失"));
        });
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation));
        Assert.AreEqual(EventDeliveryRollbackPhase.PauseRequested, fixture.Journal.Operation.Phase);
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.PauseRequested, EventDeliveryRollbackPhase.Recovering);
        fixture.Options.Streams[0].ConnectorName = "new-config-must-not-be-used";
        await fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation);
        await fixture.Connect.Received(1).ResumeConnectorAsync("orders", Arg.Any<CancellationToken>());
        await fixture.Connect.DidNotReceive().ResumeConnectorAsync("new-config-must-not-be-used", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Resume_http_success_without_running_tasks_does_not_report_recovery()
    {
        await using var fixture = new ControlFixture();
        await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation);
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Paused, EventDeliveryRollbackPhase.Recovering);
        fixture.Connect.WaitForConnectorHealthyAsync("orders", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(false);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation));
        Assert.AreEqual(EventDeliveryRollbackPhase.Recovering, fixture.Journal.Operation.Phase);
    }

    [TestMethod]
    public async Task Completed_generation_cannot_resume_the_old_connector()
    {
        await using var fixture = new ControlFixture();
        fixture.Journal.Operation = fixture.Journal.Operation with { Phase = EventDeliveryRollbackPhase.Completed };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation));
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Changed_control_plane_address_keeps_recovery_closed()
    {
        await using var fixture = new ControlFixture();
        await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation);
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Paused, EventDeliveryRollbackPhase.Recovering);
        fixture.Options.ConnectBaseUri = "http://another-connect:8083";
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation));
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Shared_connector_mapping_is_rejected_before_pause()
    {
        await using var fixture = new ControlFixture();
        fixture.Options.Streams = [fixture.Options.Streams[0], new()
        {
            EventType = "fullnet.messaging.other.changed", SchemaVersion = 1, ConnectorName = "orders",
            TopicName = "messaging.other.v1", ConsumerGroupId = "fullnet.messaging.other",
        }];
        var proof = await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation);
        Assert.IsFalse(proof.ConnectorStopped);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Connector_configuration_change_prevents_resuming_a_different_resource()
    {
        await using var fixture = new ControlFixture();
        await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation);
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Paused, EventDeliveryRollbackPhase.Recovering);
        var changed = ControlFixture.ConnectorConfiguration();
        changed["database.hostname"] = "another-database-server";
        fixture.Connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(changed);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation));
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Failure_before_pause_confirms_running_without_sending_resume()
    {
        await using var fixture = new ControlFixture();
        fixture.Connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns(CdcDeliveryPosition.ForMySql(null, "mysql-bin.000001", 1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, cancellation.Token));
        Assert.AreEqual(EventDeliveryRollbackPhase.Prepared, fixture.Journal.Operation.Phase);
        Assert.IsFalse(fixture.Journal.Operation.HasPauseIntent);
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Prepared, EventDeliveryRollbackPhase.Recovering);
        await fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation);
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Other_default_cdc_owner_is_rejected_even_without_a_persisted_row()
    {
        await using var fixture = new ControlFixture(includeOtherDefaultCdc: true);
        Assert.IsFalse((await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation)).ConnectorStopped);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Committed_offset_ahead_of_current_log_end_is_not_a_drain_proof()
    {
        var partition = new TopicPartition("topic", 0);
        var query = Substitute.For<IKafkaLagQueryClient>();
        query.ReadPartitionsAsync("topic").Returns(new[] { partition });
        query.ReadCommittedOffsetsAsync("group", Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(new Dictionary<TopicPartition, long> { [partition] = 2 });
        query.ReadEndOffsetsAsync(Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(new Dictionary<TopicPartition, long> { [partition] = 1 });
        Assert.IsNull(await new KafkaConsumerLagObserver(_ => query).ObserveLagAsync(new(), "topic", "group", CancellationToken.None));
    }

    [TestMethod]
    public async Task Real_http_client_preserves_pause_intent_and_requires_running_tasks_after_resume()
    {
        await using var fixture = new ControlFixture();
        using var handler = new ControlHttpHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://connect:8083") };
        using var connect = new KafkaConnectAdminClient(http);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            fixture.CreateReader(connect).PrepareAsync(ControlFixture.EventType, 1, fixture.Generation));
        Assert.IsTrue(handler.Paused);
        Assert.IsTrue(fixture.Journal.Operation.HasPauseIntent);
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.PauseRequested, EventDeliveryRollbackPhase.Recovering);
        handler.FailResumeTasks = true;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.CreateReader(connect).AbortAsync(ControlFixture.EventType, 1, fixture.Generation));
        handler.FailResumeTasks = false;
        await fixture.CreateReader(connect).AbortAsync(ControlFixture.EventType, 1, fixture.Generation);
        Assert.AreEqual(2, handler.ResumeCalls);
    }

    [TestMethod]
    public async Task Different_connect_and_broker_clusters_are_rejected_before_pause()
    {
        await using var fixture = new ControlFixture();
        fixture.Connect.TryReadKafkaClusterIdAsync(Arg.Any<CancellationToken>()).Returns("another-cluster");
        Assert.IsFalse((await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation)).ConnectorStopped);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Broker_cluster_change_during_drain_cannot_create_final_proof()
    {
        await using var fixture = new ControlFixture();
        fixture.Connect.PauseConnectorAsync("orders", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            fixture.Paused = true;
            fixture.BrokerClusterId = "changed-during-drain";
            return Task.CompletedTask;
        });
        Assert.IsFalse((await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation)).ConnectorStopped);
        Assert.AreEqual(EventDeliveryRollbackPhase.Paused, fixture.Journal.Operation.Phase);
        Assert.IsTrue(fixture.Journal.Operation.HasPauseIntent);
    }

    [TestMethod]
    public async Task Credential_rotation_does_not_change_resource_identity_or_store_credentials()
    {
        await using var fixture = new ControlFixture();
        await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation);
        Assert.IsFalse(fixture.Journal.Operation.ResourcesJson!.Contains("credential-v1", StringComparison.Ordinal));
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Paused, EventDeliveryRollbackPhase.Recovering);
        var rotated = ControlFixture.ConnectorConfiguration();
        rotated["database.password"] = "credential-v2";
        fixture.Connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(rotated);
        await fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation);
        await fixture.Connect.Received(1).ResumeConnectorAsync("orders", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Wrong_source_database_is_rejected_before_pause()
    {
        await using var fixture = new ControlFixture();
        var wrong = ControlFixture.ConnectorConfiguration();
        wrong["database.include.list"] = "another-database";
        fixture.Connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(wrong);
        Assert.IsFalse((await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation)).ConnectorStopped);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Json_payload_converter_cannot_replace_the_required_raw_event_transport()
    {
        await using var fixture = new ControlFixture();
        var configuration = ControlFixture.ConnectorConfiguration();
        configuration["value.converter"] = "org.apache.kafka.connect.json.JsonConverter";
        fixture.Connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(configuration);
        Assert.IsFalse((await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation)).ConnectorStopped);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Unknown_retry_lag_cannot_be_used_as_a_drain_proof()
    {
        await using var fixture = new ControlFixture();
        fixture.UnknownRetry = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.CreateReader()
            .PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, timeout.Token));
        Assert.AreEqual(EventDeliveryRollbackPhase.Paused, fixture.Journal.Operation.Phase);
        Assert.IsNotNull(fixture.Journal.Operation.ResourcesJson);
    }

    [TestMethod]
    public void Api_control_registration_does_not_register_hosted_consumers()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:KafkaConnectRollback:Enabled"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.AddFullNetKafkaDeliveryControl(configuration);
        Assert.IsFalse(services.Any(item => item.ServiceType == typeof(IHostedService)));
        Assert.IsTrue(services.Any(item => item.ServiceType == typeof(IEventDeliveryRollbackReadinessReader)
            && item.ImplementationType == typeof(KafkaConnectEventDeliveryRollbackReadinessReader)));
    }

    [TestMethod]
    public void Enabled_control_options_reject_duplicate_streams_and_unbounded_waits()
    {
        var validator = new KafkaConnectRollbackOptionsValidator();
        var options = ControlFixture.CreateOptions();
        Assert.IsTrue(validator.Validate(null, options).Succeeded);
        options.Streams = [options.Streams[0], options.Streams[0]];
        Assert.IsTrue(validator.Validate(null, options).Failed);
        options = ControlFixture.CreateOptions();
        options.PrepareTimeoutSeconds = int.MaxValue;
        Assert.IsTrue(validator.Validate(null, options).Failed);
    }

    private sealed class ControlFixture : IAsyncDisposable
    {
        internal const string EventType = "fullnet.messaging.order.changed";
        internal Guid Generation { get; } = Guid.CreateVersion7();
        internal MemoryJournal Journal { get; }
        internal IKafkaConnectAdminClient Connect { get; } = Substitute.For<IKafkaConnectAdminClient>();
        internal KafkaConnectRollbackOptions Options { get; } = CreateOptions();
        internal List<string> Queries { get; } = [];
        internal bool Paused { get; set; }
        internal bool UnknownRetry { get; set; }
        internal string BrokerClusterId { get; set; } = "test-cluster";
        private readonly ServiceProvider _provider;
        private readonly KafkaConsumerLagObserver _lag;

        internal ControlFixture(bool includeOtherDefaultCdc = false)
        {
            Journal = new(new(Generation, EventType, 1, EventDeliveryRollbackPhase.Prepared, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            var store = Substitute.For<IEventStreamOwnershipStore>();
            var owner = new EventStreamOwnershipRecord(EventType, 1, "messaging.orders.v1", EventDeliveryOwner.CdcKafka,
                EventDeliveryOwner.LegacyPolling, Guid.Empty, DateTimeOffset.UtcNow, null, null, "test", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            store.FindAsync(EventType, 1, Arg.Any<CancellationToken>()).Returns(owner);
            store.ListAsync(Arg.Any<CancellationToken>()).Returns(new[] { owner });
            var topic = IntegrationEventTopicDefinition.Create("messaging.orders.v1", EventType, 1, EventDeliveryOwner.LegacyPolling);
            var subscriptions = new[] { Subscription("fullnet.messaging.orders.a"), Subscription("fullnet.messaging.orders.b") };
            var catalog = new IntegrationEventSubscriptionCatalog([topic], subscriptions);
            var fence = Substitute.For<IEventDeliveryProducerFencePositionReader>();
            fence.TryReadAsync(EventType, 1, Generation, Arg.Any<CancellationToken>()).Returns(new EventDeliveryProducerFenceSnapshot(
                Generation, CdcDeliveryPosition.ForMySql(null, "mysql-bin.000001", 10), Guid.CreateVersion7(), DateTimeOffset.UtcNow));
            var services = new ServiceCollection();
            services.AddSingleton<IEventDeliveryRollbackJournal>(Journal);
            services.AddSingleton<IEventStreamOwnershipStore>(store);
            services.AddSingleton<IIntegrationEventSubscriptionCatalog>(catalog);
            services.AddSingleton(topic);
            if (includeOtherDefaultCdc) services.AddSingleton(IntegrationEventTopicDefinition.Create(
                "messaging.other.v1", "fullnet.messaging.other.changed", 1, EventDeliveryOwner.CdcKafka));
            var resolver = Substitute.For<IEffectiveEventDeliveryOwnerResolver>();
            resolver.GetDeliveryOwnersAsync(Arg.Any<IReadOnlyCollection<IntegrationEventTopicDefinition>>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<(string, int), EventDeliveryOwner> { [(EventType, 1)] = EventDeliveryOwner.CdcKafka });
            if (includeOtherDefaultCdc) resolver.GetDeliveryOwnersAsync(Arg.Any<IReadOnlyCollection<IntegrationEventTopicDefinition>>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<(string, int), EventDeliveryOwner> { [(EventType, 1)] = EventDeliveryOwner.CdcKafka, [("fullnet.messaging.other.changed", 1)] = EventDeliveryOwner.CdcKafka });
            services.AddSingleton(resolver);
            services.AddSingleton(fence);
            var environment = Substitute.For<IHostEnvironment>();
            environment.EnvironmentName.Returns("Testing");
            services.AddSingleton(environment);
            _provider = services.BuildServiceProvider();
            Connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns(CdcDeliveryPosition.ForMySql(null, "mysql-bin.000001", 10));
            Connect.WaitForConnectorHealthyAsync("orders", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
            Connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(ConnectorConfiguration());
            Connect.TryReadKafkaClusterIdAsync(Arg.Any<CancellationToken>()).Returns("test-cluster");
            Connect.PauseConnectorAsync("orders", Arg.Any<CancellationToken>()).Returns(_ => { Paused = true; return Task.CompletedTask; });
            Connect.IsConnectorPausedAsync("orders", Arg.Any<CancellationToken>()).Returns(_ => Paused);
            _lag = new(_ =>
            {
                var client = Substitute.For<IKafkaLagQueryClient>();
                client.ReadClusterIdAsync().Returns(_ => BrokerClusterId);
                client.ReadPartitionsAsync(Arg.Any<string>()).Returns(call =>
                    UnknownRetry && call.Arg<string>()!.Contains("retry", StringComparison.Ordinal)
                        ? Array.Empty<TopicPartition>() : new[] { new TopicPartition(call.Arg<string>()!, 0) });
                client.ReadCommittedOffsetsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(call =>
                {
                    var partitions = call.Arg<IReadOnlyList<TopicPartition>>()!;
                    Queries.Add(call.Arg<string>() + "|" + partitions[0].Topic);
                    return partitions.ToDictionary(item => item, _ => 0L);
                });
                client.ReadEndOffsetsAsync(Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(call =>
                    call.Arg<IReadOnlyList<TopicPartition>>()!.ToDictionary(item => item, _ => 0L));
                return client;
            });
        }

        internal KafkaConnectEventDeliveryRollbackReadinessReader CreateReader(IKafkaConnectAdminClient? connect = null) => new(
            _provider.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Options.Options.Create(Options),
            Microsoft.Extensions.Options.Options.Create(new KafkaMessagingOptions { Enabled = true, RetryStages = ["5s"] }), connect ?? Connect, _lag,
            _provider.GetRequiredService<IHostEnvironment>());

        internal static KafkaConnectRollbackOptions CreateOptions() => new()
        {
            Enabled = true, ConnectBaseUri = "http://connect:8083", DrainPollIntervalMilliseconds = 10,
            Streams = [new() { EventType = EventType, SchemaVersion = 1, ConnectorName = "orders", TopicName = "messaging.orders.v1",
                ShadowTopicName = "fullnet.shadow.orders.v1", ShadowConnectorName = "orders-shadow", ConsumerGroupId = "fullnet.messaging.orders.a", SourceDatabaseName = "test" }],
        };

        internal static Dictionary<string, string> ConnectorConfiguration() => new()
        {
            ["connector.class"] = "io.debezium.connector.mysql.MySqlConnector", ["database.hostname"] = "original-database-server",
            ["database.include.list"] = "test", ["table.include.list"] = "test.fn_messaging_outbox_event", ["database.password"] = "credential-v1",
            ["key.converter"] = "org.apache.kafka.connect.storage.StringConverter",
            ["value.converter"] = "io.debezium.converters.BinaryDataConverter",
            ["value.converter.delegate.converter.type"] = "org.apache.kafka.connect.json.JsonConverter",
            ["value.converter.delegate.converter.type.schemas.enable"] = "false",
            ["topic.heartbeat.prefix"] = "fullnet.test.internal.heartbeat.mysql", ["heartbeat.interval.ms"] = "1000",
            ["transforms"] = "outbox", ["tasks.max"] = "1", ["snapshot.mode"] = "no_data",
            ["transforms.outbox.type"] = "io.debezium.transforms.outbox.EventRouter",
            ["transforms.outbox.table.field.event.id"] = "Id", ["transforms.outbox.table.field.event.key"] = "PartitionKey",
            ["transforms.outbox.table.field.event.type"] = "MessageType", ["transforms.outbox.table.field.event.payload"] = "Payload",
            ["transforms.outbox.route.by.field"] = "MessageType", ["transforms.outbox.route.topic.replacement"] = "messaging.orders.v1",
            ["transforms.outbox.table.op.invalid.behavior"] = "fatal",
            ["transforms.outbox.table.fields.additional.placement"] = "Id:header:event_id,MessageType:header:message_type,SchemaVersion:header:schema_version,ContentType:header:content_type,TenantId:header:tenant_id,CorrelationId:header:correlation_id,CausationId:header:causation_id,TraceParent:header:trace_parent,Producer:header:producer,OccurredAtUtc:header:occurred_at_utc",
        };

        private static IIntegrationEventSubscription Subscription(string group)
        {
            var subscription = Substitute.For<IIntegrationEventSubscription>();
            subscription.ConsumerName.Returns(group);
            subscription.EventType.Returns(EventType);
            subscription.SchemaVersion.Returns(1);
            subscription.IdempotencyStrategy.Returns(IntegrationEventIdempotencyStrategy.MessageIdDeduplication);
            return subscription;
        }
        public ValueTask DisposeAsync() => _provider.DisposeAsync();
    }

    private sealed class MemoryJournal(EventDeliveryRollbackOperation operation) : IEventDeliveryRollbackJournal
    {
        internal EventDeliveryRollbackOperation Operation { get; set; } = operation;
        public Task<EventDeliveryRollbackOperation?> FindAsync(Guid generation, CancellationToken cancellationToken = default) =>
            Task.FromResult<EventDeliveryRollbackOperation?>(generation == Operation.Id ? Operation : null);
        public Task<bool> BindResourcesAsync(Guid generation, string resourcesJson, CancellationToken cancellationToken = default)
        {
            if (generation != Operation.Id || Operation.Phase != EventDeliveryRollbackPhase.Prepared || Operation.ResourcesJson is not null) return Task.FromResult(false);
            Operation = Operation with { ResourcesJson = resourcesJson };
            return Task.FromResult(true);
        }
        public Task<bool> TransitionAsync(Guid generation, EventDeliveryRollbackPhase expected, EventDeliveryRollbackPhase next,
            CancellationToken cancellationToken = default)
        {
            if (generation != Operation.Id || Operation.Phase != expected) return Task.FromResult(false);
            Operation = Operation with { Phase = next, HasPauseIntent = Operation.HasPauseIntent || next == EventDeliveryRollbackPhase.PauseRequested };
            return Task.FromResult(true);
        }
    }

    private sealed class ControlHttpHandler : HttpMessageHandler
    {
        internal bool Paused { get; private set; }
        internal bool FailResumeTasks { get; set; }
        internal int ResumeCalls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/pause", StringComparison.Ordinal))
            {
                Paused = true;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
            }
            if (path.EndsWith("/resume", StringComparison.Ordinal))
            {
                ResumeCalls++;
                Paused = false;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Accepted));
            }
            var json = path == "/" ? "{\"kafka_cluster_id\":\"test-cluster\"}"
                : path.EndsWith("/config", StringComparison.Ordinal) ? System.Text.Json.JsonSerializer.Serialize(ControlFixture.ConnectorConfiguration())
                : path.EndsWith("/offsets", StringComparison.Ordinal) ? "{\"offsets\":[{\"offset\":{\"file\":\"mysql-bin.000001\",\"pos\":10}}]}"
                : Paused ? "{\"connector\":{\"state\":\"PAUSED\"},\"tasks\":[{\"state\":\"PAUSED\"}]}"
                : FailResumeTasks ? "{\"connector\":{\"state\":\"RUNNING\"},\"tasks\":[{\"state\":\"FAILED\"}]}"
                : "{\"connector\":{\"state\":\"RUNNING\"},\"tasks\":[{\"state\":\"RUNNING\"}]}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
