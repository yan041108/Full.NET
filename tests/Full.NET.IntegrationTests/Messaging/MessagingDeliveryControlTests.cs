using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;
using Full.NET.Modules.Messaging.Features.RollbackDeliveryOwner;
using Full.NET.Modules.Messaging.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>真实双库验证持久恢复、结果未知和切流证明；外部控制面由故障模型提供。</summary>
[TestClass]
public sealed class MessagingDeliveryControlTests
{
    [TestMethod]
    public async Task SqlServer_persisted_recovery_and_cutover_evidence_fail_closed() =>
        await VerifyAsync(new() { Provider = DatabaseProvider.SqlServer,
            ConnectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16 });

    [TestMethod]
    public async Task MySql_persisted_recovery_and_cutover_evidence_fail_closed() =>
        await VerifyAsync(new() { Provider = DatabaseProvider.MySql,
            ConnectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16 });

    private static async Task VerifyAsync(DatabaseOptions options)
    {
        var control = new ControlState();
        var probe = new ProbeState();
        var faults = new CommitFaultState();
        await using var connection = await OpenAsync(options);
        await using (var provider = await BuildAsync(options, control, probe, faults))
        {
            await using var scope = EventDeliveryPilotTestSupport.CreateHostScope(provider);
            var cutover = scope.ServiceProvider.GetRequiredService<DeliveryCutoverService>();
            Assert.AreEqual(MessagingErrorCodes.CutoverPreconditionFailed,
                (await cutover.CutoverAsync(Cutover())).Error?.Code, "开关不能替代持久证明。");
            var evidence = scope.ServiceProvider.GetRequiredService<DeliveryCutoverEvidenceService>();
            Assert.IsFalse((await evidence.VerifyAsync(new(EventType, 1, "missing sample"), CancellationToken.None)).IsSuccess);
            await EventDeliveryPilotTestSupport.SeedCutoverEvidenceAsync(provider);
            probe.Fingerprint = "changed-configuration";
            Assert.IsFalse((await cutover.CutoverAsync(Cutover())).IsSuccess);
            probe.Fingerprint = "test-resource-fingerprint";
            await connection.ExecuteAsync("UPDATE fn_messaging_cutover_evidence SET ExpiresAtUtc = VerifiedAtUtc WHERE EventType = @EventType", new { EventType });
            Assert.IsFalse((await cutover.CutoverAsync(Cutover())).IsSuccess);
            Assert.IsTrue((await evidence.VerifyAsync(new(EventType, 1, "renew evidence"), CancellationToken.None)).IsSuccess);
            await connection.ExecuteAsync("UPDATE fn_messaging_shadow_event SET PartitionKey = @Key WHERE MessageType = @EventType",
                new { EventType, Key = "changed-after-verification" });
            Assert.IsFalse((await cutover.CutoverAsync(Cutover())).IsSuccess, "证明不能覆盖验证后改变的源数据。");
            Assert.IsTrue((await evidence.VerifyAsync(new(EventType, 1, "verify current source"), CancellationToken.None)).IsSuccess);
            await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(provider, CancellationToken.None);
            Assert.AreEqual(MessagingErrorCodes.LegacyBacklogNotDrained, (await cutover.CutoverAsync(Cutover())).Error?.Code);
            await EventDeliveryPilotTestSupport.ProcessOutboxOnceAsync(provider, CancellationToken.None);
            await connection.ExecuteAsync("""
                INSERT INTO fn_messaging_stream_ownership
                (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner, CutoffEventId, CutoffOccurredAtUtc,
                 CdcSourcePositionJson, OperatorUserId, Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc,
                 RollbackState, RollbackGeneration, RollbackPreparedAtUtc, CreatedAtUtc, UpdatedAtUtc)
                SELECT 'fullnet.messaging.other.changed', 1, 'messaging.other.v1', 2, 0, CutoffEventId, CutoffOccurredAtUtc,
                       NULL, OperatorUserId, Reason, NULL, NULL, 0, NULL, NULL, CreatedAtUtc, UpdatedAtUtc
                FROM fn_messaging_stream_ownership WHERE MessageType = @EventType
                """, new { EventType });
            Assert.IsFalse((await cutover.CutoverAsync(Cutover())).IsSuccess, "单流控制协议不能隐式切换共享 Outbox 中的多个活动流。");
            await connection.ExecuteAsync("DELETE FROM fn_messaging_stream_ownership WHERE MessageType = 'fullnet.messaging.other.changed' AND SchemaVersion = 1");
            Assert.IsTrue((await cutover.CutoverAsync(Cutover())).IsSuccess);

            control.FailPrepare = true;
            control.FailResume = true;
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<DeliveryRollbackService>().RollbackAsync(Rollback()));
            Assert.IsTrue(control.IsPaused);
            Assert.AreEqual(1, await FenceAsync(connection));
            Assert.AreEqual((int)EventDeliveryRollbackPhase.Recovering, await PhaseAsync(connection, control.Generation));
            await Assert.ThrowsExactlyAsync<EventDeliveryProducerFencedException>(() =>
                EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(provider, CancellationToken.None));
        }

        // 销毁原容器与作用域；新实例只依赖数据库日志和外部状态，不能复用原进程登记。
        await using (var restarted = await BuildAsync(options, control, probe, faults))
        {
            await using var scope = EventDeliveryPilotTestSupport.CreateHostScope(restarted);
            var recovery = scope.ServiceProvider.GetRequiredService<DeliveryRollbackRecoveryService>();
            var latest = await recovery.FindLatestAsync(EventType, 1, CancellationToken.None);
            Assert.AreEqual(control.Generation, latest.Value?.Generation);
            var calls = control.ResumeCalls;
            await connection.ExecuteAsync("UPDATE fn_messaging_stream_ownership SET RollbackGeneration = @Generation WHERE MessageType = @EventType",
                new { EventType, Generation = Guid.CreateVersion7() });
            Assert.IsFalse((await recovery.RecoverAsync(control.Generation, new("wrong generation"), CancellationToken.None)).IsSuccess);
            Assert.AreEqual(calls, control.ResumeCalls);
            await connection.ExecuteAsync("UPDATE fn_messaging_stream_ownership SET RollbackGeneration = @Generation WHERE MessageType = @EventType",
                new { EventType, control.Generation });
            control.FailResume = false;
            var recovered = await recovery.RecoverAsync(control.Generation, new("recover after restart"), CancellationToken.None);
            Assert.IsTrue(recovered.IsSuccess);
            Assert.AreEqual(EventDeliveryRollbackPhase.Recovered, recovered.Value!.Phase);
            Assert.AreEqual(0, await FenceAsync(connection));
            calls = control.ResumeCalls;
            Assert.IsTrue((await recovery.RecoverAsync(control.Generation, new("idempotent retry"), CancellationToken.None)).IsSuccess);
            Assert.AreEqual(calls, control.ResumeCalls);
            await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(restarted, CancellationToken.None);

            // 完成事务真实提交后抛出响应丢失；新会话必须发现 Completed，不能 resume。
            control.FailPrepare = false;
            faults.FailCompletionOnce = true;
            await Assert.ThrowsExactlyAsync<CommitResponseLostException>(() =>
                scope.ServiceProvider.GetRequiredService<DeliveryRollbackService>().RollbackAsync(Rollback()));
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType = @EventType", new { EventType }));
            Assert.AreEqual((int)EventDeliveryRollbackPhase.Completed, await PhaseAsync(connection, control.Generation));
            Assert.AreEqual(calls, control.ResumeCalls, "已经切回 Legacy 时恢复旧 Connector 会破坏单一所有权。");
            Assert.IsTrue(control.IsPaused);
            Assert.IsTrue((await recovery.RecoverAsync(control.Generation, new("query committed operation"), CancellationToken.None)).IsSuccess);
            Assert.AreEqual(calls, control.ResumeCalls);

            // 构造下一次 CdcKafka 试点；这里只验证准备提交结果未知，独立于正式切流端到端验证。
            await connection.ExecuteAsync("UPDATE fn_messaging_stream_ownership SET CurrentOwner = 2, PreviousOwner = 0 WHERE MessageType = @EventType", new { EventType });
            control.IsPaused = false;
            faults.FailNextCommit = true;
            await Assert.ThrowsExactlyAsync<CommitResponseLostException>(() =>
                scope.ServiceProvider.GetRequiredService<DeliveryRollbackService>().RollbackAsync(Rollback()));
            latest = await recovery.FindLatestAsync(EventType, 1, CancellationToken.None);
            Assert.IsTrue(latest.IsSuccess);
            Assert.AreEqual(EventDeliveryRollbackPhase.Prepared, latest.Value!.Phase);
            Assert.AreEqual(1, await FenceAsync(connection));
            calls = control.ResumeCalls;
            Assert.IsTrue((await recovery.RecoverAsync(latest.Value.Generation, new("recover unknown begin result"), CancellationToken.None)).IsSuccess);
            Assert.AreEqual(calls, control.ResumeCalls, "从未请求暂停的操作不得主动 resume。");
            Assert.AreEqual(0, await FenceAsync(connection));
            if (options.Provider == DatabaseProvider.SqlServer)
                await VerifyCapturedStreamFenceQueryAsync(connection, scope.ServiceProvider);
        }

        foreach (var file in new[] { "291_MessagingRollbackOperation.sql", "292_MessagingCutoverEvidence.sql" })
        {
            var root = Api.FullNetApiFactory.FindRepositoryRoot();
            var sql = await File.ReadAllTextAsync(Path.Combine(root, "src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Migrations", options.Provider.ToString(), file));
            await connection.ExecuteAsync(sql);
            await connection.ExecuteAsync(sql);
        }
        await VerifyBrokerEvidenceAsync(connection, options);
    }

    /// <summary>真实 SQL 执行验证捕获完整性查询；CT 数据是测试投影，不代表 Agent 或 Debezium 已验收。</summary>
    private static async Task VerifyCapturedStreamFenceQueryAsync(DbConnection connection, IServiceProvider services)
    {
        var generation = Guid.CreateVersion7();
        await connection.ExecuteAsync("IF SCHEMA_ID('cdc') IS NULL EXEC('CREATE SCHEMA cdc');");
        await connection.ExecuteAsync("CREATE TABLE cdc.fullnet_fn_messaging_outbox_event_CT (Id uniqueidentifier NOT NULL, __$start_lsn binary(10) NOT NULL, __$operation int NOT NULL)");
        await connection.ExecuteAsync("UPDATE fn_messaging_stream_ownership SET RollbackState = 1, RollbackGeneration = @Generation WHERE MessageType = @EventType",
            new { EventType, Generation = generation });
        try
        {
            var reader = services.GetRequiredService<IEventDeliveryProducerFencePositionReader>();
            Assert.IsNull(await reader.TryReadAsync(EventType, 1, generation), "全库已有位点不能替代指定流的完整捕获。");
            await connection.ExecuteAsync("""
                INSERT INTO cdc.fullnet_fn_messaging_outbox_event_CT (Id, __$start_lsn, __$operation)
                SELECT Id, 0x00000027000001230001, 2 FROM fn_messaging_outbox_event WHERE MessageType = @EventType
                """, new { EventType });
            var captured = await reader.TryReadAsync(EventType, 1, generation);
            Assert.IsNotNull(captured);
            Assert.AreEqual("00000027:00000123:0001", captured.ProducerFencePosition.Lsn?.CommitLsn);
            await connection.ExecuteAsync("DELETE TOP (1) FROM cdc.fullnet_fn_messaging_outbox_event_CT");
            Assert.IsNull(await reader.TryReadAsync(EventType, 1, generation), "遗漏任一冻结源行时必须保留生产者保护。");
        }
        finally
        {
            await connection.ExecuteAsync("UPDATE fn_messaging_stream_ownership SET RollbackState = 0, RollbackGeneration = NULL WHERE MessageType = @EventType", new { EventType });
            await connection.ExecuteAsync("DROP TABLE cdc.fullnet_fn_messaging_outbox_event_CT");
        }
    }

    private static async Task VerifyBrokerEvidenceAsync(DbConnection connection, DatabaseOptions database)
    {
        var kafka = await KafkaFixture.GetOrStartAsync();
        var suffix = Guid.NewGuid().ToString("N");
        const string formalTopic = "organization.unit-changed.v1";
        var shadowTopic = "fullnet.messaging.batch2.shadow.s" + suffix + ".v1";
        var group = "fullnet.messaging.batch2.g" + suffix;
        var kafkaOptions = kafka.CreateOptions("fullnet.messaging.batch2");
        kafkaOptions.ConsumerInstanceId = suffix;
        kafkaOptions.RetryStages = ["5s"];
        await kafka.EnsureTopicsAsync(shadowTopic, formalTopic, formalTopic + ".retry.5s");
        var samples = (await connection.QueryAsync<CutoverShadowEvent>(
            "SELECT Id, PartitionKey, Payload, OccurredAtUtc, ContentType, TenantId, CorrelationId, CausationId, TraceParent, Producer, CaptureGeneration FROM fn_messaging_shadow_event WHERE MessageType = @EventType AND SchemaVersion = 1 ORDER BY Id",
            new { EventType })).ToArray();
        Assert.IsTrue(samples.Length > 0);
        using var producer = kafka.CreateProducer("fullnet.messaging.batch2.publisher");
        foreach (var sample in samples) await producer.ProduceAsync(shadowTopic, Message(sample));
        using var stop = new CancellationTokenSource();
        var member = Task.Factory.StartNew(() =>
        {
            using var consumer = new ConsumerBuilder<string, byte[]>(kafkaOptions.BuildConsumerConfig(group)).Build();
            consumer.Subscribe([formalTopic, formalTopic + ".retry.5s"]);
            while (!stop.IsCancellationRequested) _ = consumer.Consume(TimeSpan.FromMilliseconds(50));
            consumer.Close();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            var databaseName = database.Provider == DatabaseProvider.SqlServer ? SharedDatabaseFixture.GetSqlServerDatabaseName(database.ConnectionString)
                : SharedDatabaseFixture.GetMySqlDatabaseName(database.ConnectionString);
            var config = ConnectorConfiguration(database.Provider, databaseName, formalTopic);
            using var admin = new AdminClientBuilder(kafkaOptions.BuildClientConfig()).Build();
            var cluster = await admin.DescribeClusterAsync(new DescribeClusterOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
            using var handler = new ConnectorReadinessHandler(config, ConnectorConfiguration(database.Provider, databaseName, shadowTopic, true), cluster.ClusterId);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://connect:8083") };
            using var connect = new KafkaConnectAdminClient(http);
            var binding = new KafkaConnectRollbackStreamBinding
            {
                EventType = EventType, SchemaVersion = 1, TopicName = formalTopic, ShadowTopicName = shadowTopic,
                ConnectorName = "pilot", ShadowConnectorName = "pilot-shadow", ConsumerGroupId = group, SourceDatabaseName = databaseName, WorkerClientId = kafkaOptions.ClientId!,
            };
            var apiOptions = kafka.CreateOptions("fullnet.messaging.api-verifier");
            apiOptions.RetryStages = ["5s"];
            apiOptions.ConsumerInstanceId = null;
            var probe = new KafkaEventDeliveryCutoverProbe(Options.Create(new KafkaConnectRollbackOptions
            {
                Enabled = true, ConnectBaseUri = "http://connect:8083", Streams = [binding],
            }), Options.Create(apiOptions), new EvidenceEnvironment(), new IntegrationEventSubscriptionCatalog(
                [IntegrationEventTopicDefinition.Create(formalTopic, EventType, 1, EventDeliveryOwner.LegacyPolling)], [new NeverProcessSubscription(group)]),
                connect, new KafkaReplayConsumerFactory(Options.Create(apiOptions)));
            string? fingerprint = null;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (fingerprint is null && DateTimeOffset.UtcNow < deadline)
            {
                try { fingerprint = await probe.ReadTargetFingerprintAsync(EventType, 1); }
                catch (KafkaException) { }
                if (fingerprint is null) await Task.Delay(100);
            }
            Assert.IsNotNull(fingerprint, "真实消费组必须稳定拥有正式 Topic 与重试 Topic 的全部分区。");
            Assert.IsNotNull(await probe.CompareShadowAsync(EventType, 1, samples));
            config["database.user"] = "another-deployment-user";
            Assert.AreNotEqual(fingerprint, await probe.ReadTargetFingerprintAsync(EventType, 1));
            config["transforms.outbox.route.topic.replacement"] = "wrong.target.v1";
            Assert.IsNull(await probe.ReadTargetFingerprintAsync(EventType, 1), "任意健康 Connector 不能替代正确目标路由。");
            config["transforms.outbox.route.topic.replacement"] = formalTopic;
            await producer.ProduceAsync(shadowTopic, Message(samples[0]));
            Assert.IsNotNull(await probe.CompareShadowAsync(EventType, 1, samples), "完全相同的至少一次副本不得重复计数或阻止证明。");
        }
        finally { stop.Cancel(); await member; }
    }

    private static Message<string, byte[]> Message(CutoverShadowEvent sample)
    {
        var headers = new Headers
        {
            { "capture_generation", Encoding.UTF8.GetBytes(sample.CaptureGeneration.ToString("D")) },
            { KafkaEnvelopeHeaderNames.EventId, Encoding.UTF8.GetBytes(sample.Id.ToString("D")) },
            { KafkaEnvelopeHeaderNames.MessageType, Encoding.UTF8.GetBytes(EventType) },
            { KafkaEnvelopeHeaderNames.SchemaVersion, "1"u8.ToArray() },
            { KafkaEnvelopeHeaderNames.ContentType, Encoding.UTF8.GetBytes(sample.ContentType) },
            { KafkaEnvelopeHeaderNames.Producer, Encoding.UTF8.GetBytes(sample.Producer) },
            { KafkaEnvelopeHeaderNames.OccurredAtUtc, Encoding.UTF8.GetBytes(sample.OccurredAtUtc.ToString("O")) },
        };
        foreach (var (key, value) in new[] { (KafkaEnvelopeHeaderNames.TenantId, sample.TenantId?.ToString("D")),
            (KafkaEnvelopeHeaderNames.CorrelationId, sample.CorrelationId), (KafkaEnvelopeHeaderNames.CausationId, sample.CausationId?.ToString("D")),
            (KafkaEnvelopeHeaderNames.TraceParent, sample.TraceParent) })
            if (value is not null) headers.Add(key, Encoding.UTF8.GetBytes(value));
        return new() { Key = sample.PartitionKey, Value = sample.Payload, Headers = headers, Timestamp = new Timestamp(sample.OccurredAtUtc.UtcDateTime) };
    }

    private static Dictionary<string, string> ConnectorConfiguration(DatabaseProvider provider, string database, string topic, bool shadow = false)
    {
        var configuration = new Dictionary<string, string>
        {
        ["connector.class"] = provider == DatabaseProvider.SqlServer ? "io.debezium.connector.sqlserver.SqlServerConnector" : "io.debezium.connector.mysql.MySqlConnector",
        [provider == DatabaseProvider.SqlServer ? "database.names" : "database.include.list"] = database,
        ["table.include.list"] = (provider == DatabaseProvider.SqlServer ? "dbo" : database) + (shadow ? ".fn_messaging_shadow_event" : ".fn_messaging_outbox_event"),
        ["key.converter"] = "org.apache.kafka.connect.storage.StringConverter",
        ["value.converter"] = "io.debezium.converters.BinaryDataConverter",
        ["value.converter.delegate.converter.type"] = "org.apache.kafka.connect.json.JsonConverter",
        ["value.converter.delegate.converter.type.schemas.enable"] = "false",
        ["topic.heartbeat.prefix"] = "fullnet.test.internal.heartbeat", ["heartbeat.interval.ms"] = "1000",
        ["transforms"] = "outbox", ["tasks.max"] = "1", ["snapshot.mode"] = "no_data", ["database.user"] = "test-source-user",
        ["transforms.outbox.type"] = "io.debezium.transforms.outbox.EventRouter",
        ["transforms.outbox.table.field.event.id"] = "Id", ["transforms.outbox.table.field.event.key"] = "PartitionKey",
        ["transforms.outbox.table.field.event.type"] = "MessageType", ["transforms.outbox.table.field.event.payload"] = "Payload",
        ["transforms.outbox.route.by.field"] = "MessageType", ["transforms.outbox.route.topic.replacement"] = topic,
        ["transforms.outbox.table.op.invalid.behavior"] = "fatal",
        ["transforms.outbox.table.fields.additional.placement"] = "Id:header:event_id,MessageType:header:message_type,SchemaVersion:header:schema_version,ContentType:header:content_type,TenantId:header:tenant_id,CorrelationId:header:correlation_id,CausationId:header:causation_id,TraceParent:header:trace_parent,Producer:header:producer,OccurredAtUtc:header:occurred_at_utc" + (shadow ? ",CaptureGeneration:header:capture_generation" : ""),
        };
        // 与真实模板保持相同的提供程序时标约束，SQL Server 不提供整数事件时间列。
        if (provider == DatabaseProvider.MySql) configuration["transforms.outbox.table.field.event.timestamp"] = "OccurredAtUtc";
        return configuration;
    }

    private sealed class ConnectorReadinessHandler(Dictionary<string, string> config, Dictionary<string, string> shadowConfig, string clusterId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath == "/" ? JsonSerializer.Serialize(new { kafka_cluster_id = clusterId })
                    : request.RequestUri.AbsolutePath.EndsWith("/config", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(request.RequestUri.AbsolutePath.Contains("pilot-shadow", StringComparison.Ordinal) ? shadowConfig : config) : "{\"connector\":{\"state\":\"RUNNING\"},\"tasks\":[{\"id\":0,\"state\":\"RUNNING\"}]}"),
            });
    }
    private sealed class EvidenceEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "MessagingDeliveryControlTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class NeverProcessSubscription(string consumerName) : IIntegrationEventSubscription
    {
        public string ConsumerName => consumerName;
        public string EventType => MessagingDeliveryControlTests.EventType;
        public int SchemaVersion => 1;
        public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.MessageIdDeduplication;
        public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("影子证明不得执行任何业务 Handler。");
    }

    private static string EventType => EventDeliveryPilotTestSupport.PilotEventType;
    private static ChangeDeliveryOwnerRequest Cutover() => new(EventType, 1, EventDeliveryOwner.CdcKafka, "verified pilot cutover");
    private static ChangeDeliveryOwnerRequest Rollback() => new(EventType, 1, EventDeliveryOwner.LegacyPolling, "verified pilot rollback");
    private static Task<int> FenceAsync(DbConnection connection) => connection.ExecuteScalarAsync<int>(
        "SELECT RollbackState FROM fn_messaging_stream_ownership WHERE MessageType = @EventType", new { EventType });
    private static Task<int> PhaseAsync(DbConnection connection, Guid generation) => connection.ExecuteScalarAsync<int>(
        "SELECT Phase FROM fn_messaging_rollback_operation WHERE Id = @Generation", new { Generation = generation });
    private static Task<ServiceProvider> BuildAsync(DatabaseOptions options, ControlState control, ProbeState probe, CommitFaultState faults) =>
        EventDeliveryPilotTestSupport.BuildPilotServicesAsync(options, seedCutoverEvidence: false, configureServices: services =>
        {
            services.RemoveAll<IEventDeliveryCutoverProbe>();
            services.AddSingleton<IEventDeliveryCutoverProbe>(probe);
            services.RemoveAll<IEventDeliveryRollbackReadinessReader>();
            services.AddScoped<IEventDeliveryRollbackReadinessReader>(provider => new ModeledReadinessReader(
                provider.GetRequiredService<IEventDeliveryRollbackJournal>(), control));
            var transactionFactory = services.Single(item => item.ServiceType == typeof(ICommandTransaction)).ImplementationFactory!;
            services.Replace(ServiceDescriptor.Scoped<ICommandTransaction>(provider =>
                new CommitResponseFaultTransaction((ICommandTransaction)transactionFactory(provider), faults)));
        });

    private static async Task<DbConnection> OpenAsync(DatabaseOptions options)
    {
        DbConnection connection = options.Provider == DatabaseProvider.SqlServer ? new SqlConnection(options.ConnectionString)
            : new MySqlConnection(new MySqlConnectionStringBuilder(options.ConnectionString) { GuidFormat = MySqlGuidFormat.Binary16, AllowUserVariables = true }.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private sealed class ProbeState : IEventDeliveryCutoverProbe
    {
        internal string Fingerprint { get; set; } = "test-resource-fingerprint";
        public Task<string?> ReadTargetFingerprintAsync(string eventType, int schemaVersion, CancellationToken cancellationToken = default) => Task.FromResult<string?>(Fingerprint);
        public Task<string?> CompareShadowAsync(string eventType, int schemaVersion, IReadOnlyList<CutoverShadowEvent> expected,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>("modeled-shadow-position-digest");
    }

    private sealed class ControlState
    {
        internal Guid Generation { get; set; }
        internal bool IsPaused { get; set; }
        internal bool FailPrepare { get; set; }
        internal bool FailResume { get; set; }
        internal int ResumeCalls { get; set; }
    }

    private sealed class ModeledReadinessReader(IEventDeliveryRollbackJournal journal, ControlState state) : IEventDeliveryRollbackReadinessReader
    {
        public async Task<EventDeliveryRollbackReadiness> PrepareAsync(string eventType, int schemaVersion, Guid rollbackGeneration,
            CancellationToken cancellationToken = default)
        {
            state.Generation = rollbackGeneration;
            Assert.IsTrue(await journal.BindResourcesAsync(rollbackGeneration, "{\"modeled\":true}", cancellationToken));
            Assert.IsTrue(await journal.TransitionAsync(rollbackGeneration, EventDeliveryRollbackPhase.Prepared, EventDeliveryRollbackPhase.PauseRequested, cancellationToken));
            state.IsPaused = true;
            Assert.IsTrue(await journal.TransitionAsync(rollbackGeneration, EventDeliveryRollbackPhase.PauseRequested, EventDeliveryRollbackPhase.Paused, cancellationToken));
            if (state.FailPrepare) throw new InvalidOperationException("暂停后排空失败");
            return new(rollbackGeneration, true, true, true, "{\"fence\":true}", "{\"captured\":true}", rollbackGeneration.ToString("N"), null, DateTimeOffset.UtcNow);
        }
        public Task AbortAsync(string eventType, int schemaVersion, Guid rollbackGeneration, CancellationToken cancellationToken = default)
        {
            state.ResumeCalls++;
            if (state.FailResume) throw new InvalidOperationException("恢复未确认");
            state.IsPaused = false;
            return Task.CompletedTask;
        }
    }

    private sealed class CommitFaultState
    {
        internal bool FailCompletionOnce { get; set; }
        internal bool FailNextCommit { get; set; }
    }
    private sealed class CommitResponseLostException : Exception { }
    private sealed class CommitResponseFaultTransaction(ICommandTransaction inner, CommitFaultState faults) : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => inner.ExecuteAsync(action, cancellationToken);
        public async Task<Result<T>> ExecuteResultAsync<T>(Func<CancellationToken, Task<Result<T>>> action, CancellationToken cancellationToken)
        {
            var result = await inner.ExecuteResultAsync(action, cancellationToken);
            if (result.IsSuccess && (faults.FailNextCommit || faults.FailCompletionOnce && typeof(T) == typeof(DeliveryRollbackResponse)))
            {
                faults.FailNextCommit = false;
                if (typeof(T) == typeof(DeliveryRollbackResponse)) faults.FailCompletionOnce = false;
                throw new CommitResponseLostException();
            }
            return result;
        }
    }
}
