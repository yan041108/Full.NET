extern alias workerhost;

using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Persistence;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MsSql;
using Worker = workerhost::Full.NET.Host.Worker;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>真实 HTTP、业务处理器、双库 CDC 和 Connect 完整切流/回退，禁止替换权威证明。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MessagingCutoverRollbackDebeziumTests
{
    public TestContext TestContext { get; set; } = null!;
    private const string Group = "fullnet.identity.organization-unit-projection";
    private static string EventType => EventDeliveryPilotTestSupport.PilotEventType;
    private static string Topic => IdentityIntegrationEventTopicDefinitions.OrganizationUnitChanged.TopicCode;
    private const string Shadow = "fullnet.dev.shadow.organization.unit-changed.v1";

    [TestMethod]
    public async Task MySql_business_http_cutover_and_rollback_use_real_cdc_and_recover_after_pause()
    {
        var connection = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        try { await VerifyAsync(new() { Provider = DatabaseProvider.MySql, ConnectionString = connection, MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16 }); }
        finally
        {
            // 连接串仅来自上方新建测试库；只回收本方法拥有的 UUID 库，不扫描或删除共享实例。
            var owned = new MySqlConnectionStringBuilder(connection);
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(owned.Database, "^fullnet_it_[a-f0-9]{32}$"));
            owned.UserID = "root"; owned.Password = SharedDatabaseFixture.MySqlRootPassword;
            await using var cleanup = new MySqlConnection(owned.ConnectionString);
            MySqlConnection.ClearPool(cleanup);
            await cleanup.ExecuteAsync($"DROP DATABASE `{owned.Database}`");
        }
    }

    [TestMethod]
    public async Task SqlServer_business_http_cutover_and_rollback_use_real_agent_and_recover_after_pause()
    {
        await using var server = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword(SharedDatabaseFixture.MySqlRootPassword).WithEnvironment("MSSQL_AGENT_ENABLED", "true")
            .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048").WithCreateParameterModifier(parameters =>
            {
                var limits = parameters.HostConfig ??= new();
                limits.Memory = 3L << 30; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000L;
                if (SharedDatabaseFixture.BoundedContainers) SharedDatabaseFixture.ApplyBoundedOwnership(parameters.Labels);
            }).Build();
        await server.StartAsync();
        var connection = new SqlConnectionStringBuilder(server.GetConnectionString());
        await using (var admin = new SqlConnection(connection.ConnectionString))
        {
            connection.InitialCatalog = "fullnet_control_" + Guid.NewGuid().ToString("N");
            await admin.ExecuteAsync($"CREATE DATABASE [{connection.InitialCatalog}]");
        }
        await VerifyAsync(new() { Provider = DatabaseProvider.SqlServer, ConnectionString = connection.ConnectionString });
    }

    private async Task VerifyAsync(DatabaseOptions options)
    {
        Step(options, "migrate");
        await MessagingOutboxTestSupport.MigrateAsync(options);
        await using DbConnection database = options.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(options.ConnectionString) : new MySqlConnection(MySqlConnectionStringPolicy.Create(
                options.ConnectionString, options.MySqlGuidStorageMode, allowUserVariables: false));
        await database.OpenAsync();
        if (options.Provider == DatabaseProvider.MySql)
        {
            // 300 的过程分隔符修复须覆盖 DDL 已存在、journal 未记账时的恢复，不只验证空库。
            await database.ExecuteAsync("DROP INDEX uq_fn_workflow_task_member_sequence ON fn_workflow_task_member; DELETE FROM schemaversions WHERE ScriptName LIKE '%300_WorkflowHumanTasks.sql'");
            await MessagingOutboxTestSupport.MigrateAsync(options);
            Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_task_member' AND INDEX_NAME='uq_fn_workflow_task_member_sequence'"));
        }
        if (options.Provider == DatabaseProvider.SqlServer)
        {
            var enabled = await SqlServerCdcTestSupport.TryEnableCdcAsync(options.ConnectionString);
            Assert.IsTrue(enabled.Succeeded, enabled.FailureReason);
            await database.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "deploy/messaging/sqlserver/enable-shadow-cdc.sql")));
        }
        else Assert.IsTrue((await CdcShadowFixture.ReadMySqlBinlogStatusAsync(options.ConnectionString)).IsRowFullEnabled);
        var pipeline = await CdcDebeziumPipelineFixture.GetOrStartAsync();
        using var connect = pipeline.CreateConnectAdminClient();
        Assert.IsTrue(await connect.WaitUntilReadyAsync(TimeSpan.FromSeconds(90)));
        var name = "fullnet-control-" + Guid.NewGuid().ToString("N");
        var shadowName = name + "-shadow";
        var kafka = CdcDebeziumE2ESupport.CreateKafkaOptions(pipeline, "fullnet.real-control.worker");
        kafka.ConsumerInstanceId = null;
        using var broker = new AdminClientBuilder(kafka.BuildClientConfig()).Build();
        var topics = new[] { Topic, Shadow }.Concat(kafka.RetryStages.Select(stage => KafkaTopicNames.GetRetryTopic(Topic, stage))).ToArray();
        foreach (var topic in topics)
        {
            try { await broker.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]); }
            catch (CreateTopicsException exception) when (exception.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists)) { }
        }
        var source = options.Provider == DatabaseProvider.SqlServer
            ? new SqlConnectionStringBuilder(options.ConnectionString).InitialCatalog : new MySqlConnectionStringBuilder(options.ConnectionString).Database;
        var settings = Settings(pipeline, source, name, shadowName, kafka.ClientId!);
        var fault = new PauseAuditFault();
        await using var factory = new FullNetApiFactory(options.Provider, options.ConnectionString, settings,
            configureTestServices: services =>
            {
                services.AddScoped<MessagingDomainAuditWriter>();
                services.RemoveAll<ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>>();
                services.AddScoped<ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>>(sp => new AuditFaultWriter(sp.GetRequiredService<MessagingDomainAuditWriter>(), fault));
                services.AddSingleton<KafkaConnectEventDeliveryRollbackReadinessReader>();
                services.RemoveAll<IEventDeliveryRollbackReadinessReader>();
                services.AddSingleton<IEventDeliveryRollbackReadinessReader>(sp => new PausedGateReader(sp.GetRequiredService<KafkaConnectEventDeliveryRollbackReadinessReader>(), fault));
            });
        Step(options, "initialize HTTP and genuine consumers");
        await factory.InitializeAsync(useSchemaTemplate: false);
        using var client = factory.CreateClientForHost("localhost"); client.Timeout = TimeSpan.FromMinutes(10);
        var token = await FullNetApiFactoryLogin.LoginAsHostAdminAsync(client, default);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var business = factory.CreateClientForHost("localhost"); business.Timeout = TimeSpan.FromMinutes(2);
        var tenantHost = await FullNetApiFactoryLogin.LoginAsHostAdminAsync(business, default);
        var tenant = await FullNetApiFactoryLogin.EnterAcmeTenantAsync(business, tenantHost, default);
        business.DefaultRequestHeaders.Authorization = new("Bearer", tenant.AccessToken);

        // 单独拥有生产 Worker 组件的生命周期；API 不注册或隐式启动常驻 Kafka Consumer。
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var kafkaOptions = Options.Create(kafka);
        using var producer = new KafkaMessagingProducer(kafkaOptions);
        using var kafkaWorker = new KafkaConsumerWorker(kafkaOptions, scopeFactory,
            new KafkaConsumerMessageProcessor(kafkaOptions, scopeFactory, new KafkaEnvelopeReader(), new KafkaOffsetCommitter(), new KafkaFailureClassifier(),
                new KafkaRetryRouter(kafkaOptions, producer, NullLogger<KafkaRetryRouter>.Instance),
                new KafkaDeadLetterPublisher(kafkaOptions, producer, NullLogger<KafkaDeadLetterPublisher>.Instance), NullLogger<KafkaConsumerMessageProcessor>.Instance),
            NullLogger<KafkaConsumerWorker>.Instance);
        using var legacy = new Worker.OutboxProcessor(scopeFactory, factory.Services.GetRequiredService<IClock>(),
            Options.Create(new Worker.OutboxWorkerOptions { PollMilliseconds = 100, MaximumIdlePollMilliseconds = 100, BatchSize = 20 }), NullLogger<Worker.OutboxProcessor>.Instance);
        await legacy.StartAsync(default); await kafkaWorker.StartAsync(default);
        try
        {
            var formal = await ConnectorAsync(options, pipeline, useFormal: true);
            formal["transforms.outbox.route.topic.replacement"] = Topic;
            var diagnostic = await ConnectorAsync(options, pipeline, useFormal: false);
            Assert.AreEqual(Shadow, diagnostic["transforms.outbox.route.topic.replacement"]);
            Step(options, "register real formal and shadow connectors");
            await RegisterAsync(name, formal); await RegisterAsync(shadowName, diagnostic);
            await WaitAsync(async () =>
            {
                var description = await broker.DescribeConsumerGroupsAsync([Group]);
                return description.ConsumerGroupDescriptions.Single().State == ConsumerGroupState.Stable;
            }, "实际业务消费组就绪");

            using (var anonymous = factory.CreateClientForHost("localhost"))
            using (var denied = await anonymous.PostAsJsonAsync("/api/v1/messaging/delivery/cutover", Change(EventDeliveryOwner.CdcKafka)))
                Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
            using (var denied = await business.PostAsJsonAsync("/api/v1/messaging/delivery/cutover", Change(EventDeliveryOwner.CdcKafka)))
                Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var noEvidence = await client.PostAsJsonAsync("/api/v1/messaging/delivery/cutover", Change(EventDeliveryOwner.CdcKafka)))
            {
                var body = await noEvidence.Content.ReadAsStringAsync();
                Assert.AreEqual(HttpStatusCode.UnprocessableEntity, noEvidence.StatusCode, body);
                using var problem = JsonDocument.Parse(body);
                Assert.AreEqual(MessagingErrorCodes.CutoverPreconditionFailed, problem.RootElement.GetProperty("code").GetString());
            }

            Step(options, "capture genuine business HTTP Legacy sample");
            var formalBeforeCapture = await FormalBrokerEndAsync();
            await PostAsync<ShadowCaptureResponse>(client, "/api/v1/messaging/delivery/shadow-captures/",
                new StartShadowCaptureRequest(Guid.CreateVersion7(), null, EventType, 1, 1, 300, "real HTTP capture"));
            var before = await CreateUnitAsync("legacy-before");
            await WaitProjectionAsync(before);
            var sample = await database.QuerySingleAsync<Guid>("SELECT Id FROM fn_messaging_shadow_event WHERE MessageType=@Type", new { Type = EventType });
            Assert.IsNotNull(await CdcDebeziumE2ESupport.TryConsumeShadowEventAsync(pipeline, Shadow, sample, TimeSpan.FromSeconds(120)));
            Assert.AreEqual(0, await FormalCountAsync());
            Assert.AreEqual(formalBeforeCapture, await FormalBrokerEndAsync(), "Legacy 影子样本不能进入正式 Broker Topic。");
            await PostAsync<CutoverEvidenceResponse>(client, "/api/v1/messaging/delivery/cutover-evidence", new VerifyCutoverRequest(EventType, 1, "real connector and worker proof"));
            Step(options, "HTTP cutover and real CDC/Inbox business delivery");
            var cutover = await PostAsync<DeliveryCutoverResponse>(client, "/api/v1/messaging/delivery/cutover", Change(EventDeliveryOwner.CdcKafka));
            Assert.IsTrue(cutover.OwnershipPersisted); Assert.AreEqual(sample, cutover.CutoffEventId);
            Assert.AreEqual(2, await OwnerAsync());
            using (var duplicate = await client.PostAsJsonAsync("/api/v1/messaging/delivery/cutover", Change(EventDeliveryOwner.CdcKafka)))
                Assert.AreEqual(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode, await duplicate.Content.ReadAsStringAsync());
            var during = await CreateUnitAsync("cdc-after"); await WaitProjectionAsync(during);
            Assert.AreEqual(1, await FormalCountAsync());
            Assert.AreEqual(1, await InboxCountAsync());

            Step(options, "real pause, fenced business HTTP and transactional audit failure recovery");
            fault.Armed = true;
            var failingRollback = client.PostAsJsonAsync("/api/v1/messaging/delivery/rollback", Change(EventDeliveryOwner.LegacyPolling));
            var reached = await Task.WhenAny(fault.Paused.Task, failingRollback).WaitAsync(TimeSpan.FromMinutes(7));
            if (reached != fault.Paused.Task)
            {
                using var ended = await failingRollback;
                Assert.Fail("Rollback ended before the real pause gate: " + ended.StatusCode + " " + await ended.Content.ReadAsStringAsync());
            }
            try
            {
                Assert.IsTrue(await connect.IsConnectorPausedAsync(name));
                Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT RollbackState FROM fn_messaging_stream_ownership WHERE MessageType=@Type AND SchemaVersion=1", new { Type = EventType }));
                using var rejected = await business.PostAsJsonAsync("/api/v1/organization/units", new CreateOrganizationUnitRequest(null, "fenced-" + fault.Id, "must roll back", 10));
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, rejected.StatusCode, await rejected.Content.ReadAsStringAsync());
                Assert.AreEqual(0, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_organization_unit WHERE Code=@Code", new { Code = "fenced-" + fault.Id }));
            }
            finally { fault.Release.TrySetResult(); }
            using (var failed = await failingRollback)
                Assert.AreEqual(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.IsTrue(fault.AuditFailed);
            Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(name, TimeSpan.FromSeconds(120)));
            var recovered = await LatestAsync(); Assert.AreEqual(EventDeliveryRollbackPhase.Recovered, recovered.Phase);
            Assert.AreEqual(2, await OwnerAsync());
            Assert.AreEqual(0, await database.ExecuteScalarAsync<int>("SELECT RollbackState FROM fn_messaging_stream_ownership WHERE MessageType=@Type AND SchemaVersion=1", new { Type = EventType }));
            var afterRecovery = await CreateUnitAsync("cdc-recovered"); await WaitProjectionAsync(afterRecovery);
            Assert.AreEqual(2, await FormalCountAsync()); Assert.AreEqual(2, await InboxCountAsync());

            Step(options, "successful real rollback, terminal recovery idempotency and Legacy delivery");
            var rollback = await PostAsync<DeliveryRollbackResponse>(client, "/api/v1/messaging/delivery/rollback", Change(EventDeliveryOwner.LegacyPolling));
            Assert.IsTrue(rollback.OwnershipPersisted); Assert.AreNotEqual(Guid.Empty, rollback.RollbackBoundaryEventId);
            Assert.AreEqual(0, await OwnerAsync()); Assert.IsTrue(await connect.IsConnectorPausedAsync(name));
            var completed = await LatestAsync(); Assert.AreEqual(EventDeliveryRollbackPhase.Completed, completed.Phase);
            await PostAsync<RollbackOperationResponse>(client, $"/api/v1/messaging/delivery/rollback-operations/{completed.Generation}/recover", new RecoverRollbackRequest("terminal retry"));
            Assert.IsTrue(await connect.IsConnectorPausedAsync(name), "Completed 恢复请求不得恢复旧发布者。");
            var formalAfterRollback = await FormalBrokerEndAsync();
            var after = await CreateUnitAsync("legacy-after"); await WaitProjectionAsync(after);
            Assert.AreEqual(2, await FormalCountAsync()); Assert.AreEqual(2, await InboxCountAsync());
            Assert.AreEqual(formalAfterRollback, await FormalBrokerEndAsync(), "成功回退后的 Legacy 业务不能新增正式 Broker 记录。");
            Assert.AreEqual(4, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_identity_organization_unit_projection WHERE TenantId=@Tenant AND UnitId IN @Ids",
                new { Tenant = tenant.TenantId, Ids = new[] { before.Id, during.Id, afterRecovery.Id, after.Id } }));
            Assert.AreEqual(2, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type AND ProcessedAtUtc IS NOT NULL", new { Type = EventType }));
            Assert.AreEqual(0, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type AND (ProcessedAtUtc IS NULL OR DeadLetteredAtUtc IS NOT NULL)", new { Type = EventType }));
            Assert.AreEqual(0, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_stream_ownership WHERE MessageType=@Type AND OperatorUserId IS NULL", new { Type = EventType }));
            Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_domain_audit WHERE ActionKey='messaging.delivery.cutover' AND Outcome='success' AND ActorUserId IS NOT NULL"));
            Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_domain_audit WHERE ActionKey='messaging.delivery.rollback' AND Outcome='success' AND ActorUserId IS NOT NULL"), "失败的最终审计与所有权必须一起回滚，不能留下成功记录。");
            Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_domain_audit WHERE ActionKey='messaging.delivery.rollback_recovery' AND Outcome='success' AND ActorUserId IS NOT NULL"));
            Step(options, "complete");
        }
        finally
        {
            fault.Release.TrySetResult();
            using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await kafkaWorker.StopAsync(stopping.Token); await legacy.StopAsync(stopping.Token);
            await connect.DeleteConnectorAsync(name); await connect.DeleteConnectorAsync(shadowName);
        }

        async Task RegisterAsync(string connector, Dictionary<string, string> config)
        {
            // 正式/影子 Connector 并存时，测试内部 schema history 也按资源所有者隔离。
            config["schema.history.internal.kafka.topic"] += "." + connector;
            await connect.RegisterConnectorAsync(connector, CdcDebeziumConnectorTestSupport.CreateIsolatedConnectorConfig(connector, config));
            Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(connector, TimeSpan.FromSeconds(120)), "真实 Connector 必须健康，不以环境跳过替代失败。");
            await WaitAsync(async () =>
            {
                var status = await connect.TryGetConnectorStatusAsync(connector);
                if (status is not null)
                {
                    using var document = JsonDocument.Parse(status);
                    foreach (var task in document.RootElement.GetProperty("tasks").EnumerateArray())
                    {
                        if (task.GetProperty("state").GetString() != "FAILED") continue;
                        var trace = task.TryGetProperty("trace", out var value) ? value.GetString() ?? "" : "";
                        trace = trace.Replace(SharedDatabaseFixture.MySqlRootPassword, "[redacted]", StringComparison.Ordinal);
                        Assert.Fail("Real Connector task failed: " + trace[..Math.Min(3000, trace.Length)]);
                    }
                }
                return await connect.TryReadConnectorPositionAsync(connector) is not null;
            }, "Connector 初始源位点");
        }
        async Task<OrganizationUnitResponse> CreateUnitAsync(string phase) =>
            await FullNetApiFactoryOrganization.CreateUnitAsync(business, tenant.AccessToken, phase + "-" + Guid.NewGuid().ToString("N"), phase, default);
        async Task WaitProjectionAsync(OrganizationUnitResponse unit) => await WaitAsync(() => OrganizationUnitCdcKafkaEndToEndSupport.ProjectionExistsAsync(options, tenant.TenantId, unit.Id, unit.Name), "实际业务投影 " + unit.Name);
        Task<int> FormalCountAsync() => database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event WHERE MessageType=@Type", new { Type = EventType });
        Task<int> InboxCountAsync() => database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE ConsumerName=@Group", new { Group });
        Task<int> OwnerAsync() => database.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType=@Type AND SchemaVersion=1", new { Type = EventType });
        async Task<long> FormalBrokerEndAsync()
        {
            var result = await broker.ListOffsetsAsync([new TopicPartitionOffsetSpec
            {
                TopicPartition = new(Topic, 0), OffsetSpec = OffsetSpec.Latest(),
            }], new ListOffsetsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
            var offset = result.ResultInfos.Single().TopicPartitionOffsetError;
            Assert.IsFalse(offset.Error.IsError, offset.Error.ToString());
            return offset.Offset.Value;
        }
        async Task<RollbackOperationResponse> LatestAsync() => (await client.GetFromJsonAsync<RollbackOperationResponse>($"/api/v1/messaging/delivery/rollback-operations?eventType={EventType}&schemaVersion=1"))!;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string route, object request)
    {
        using var response = await client.PostAsJsonAsync(route, request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, route + ": " + await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static ChangeDeliveryOwnerRequest Change(EventDeliveryOwner owner) => new(EventType, 1, owner, "real full-chain verification");
    private static async Task<Dictionary<string, string>> ConnectorAsync(DatabaseOptions options, CdcDebeziumPipelineEnvironment pipeline, bool useFormal) =>
        new(options.Provider == DatabaseProvider.SqlServer
            ? await DebeziumConnectorTemplateFactory.CreateSqlServerShadowConfigAsync(options.ConnectionString, pipeline.HostGateway, pipeline.InternalKafkaBootstrapServers, useFormalOutbox: useFormal)
            : await DebeziumConnectorTemplateFactory.CreateMySqlShadowConfigAsync(options.ConnectionString, pipeline.HostGateway, pipeline.InternalKafkaBootstrapServers, useFormalOutbox: useFormal), StringComparer.Ordinal);
    private static Dictionary<string, string?> Settings(CdcDebeziumPipelineEnvironment pipeline, string source, string formal, string shadow, string worker) => new()
    {
        // 控制端与租户业务端各持有真实会话；第二次登录不能按默认单会话策略撤销第一个令牌。
        ["Identity:SessionLoginPolicy"] = "AllowMultiple",
        ["Messaging:DeliveryCutover:Enabled"] = "true", ["Messaging:ShadowCapture:Enabled"] = "true", ["Messaging:ShadowCapture:EventType"] = EventType,
        ["Messaging:Kafka:Enabled"] = "true", ["Messaging:Kafka:BootstrapServers"] = pipeline.BootstrapServers,
        ["Messaging:Kafka:ClientId"] = "fullnet.real-control.api", ["Messaging:Kafka:ConsumerInstanceId"] = "",
        ["Messaging:KafkaConnectRollback:Enabled"] = "true", ["Messaging:KafkaConnectRollback:ConnectBaseUri"] = pipeline.ConnectBaseUri.AbsoluteUri,
        ["Messaging:KafkaConnectRollback:PrepareTimeoutSeconds"] = "180", ["Messaging:KafkaConnectRollback:DrainTimeoutSeconds"] = "180",
        ["Messaging:KafkaConnectRollback:DrainPollIntervalMilliseconds"] = "100",
        ["Messaging:KafkaConnectRollback:Streams:0:EventType"] = EventType, ["Messaging:KafkaConnectRollback:Streams:0:SchemaVersion"] = "1",
        ["Messaging:KafkaConnectRollback:Streams:0:ConnectorName"] = formal, ["Messaging:KafkaConnectRollback:Streams:0:ShadowConnectorName"] = shadow,
        ["Messaging:KafkaConnectRollback:Streams:0:TopicName"] = Topic, ["Messaging:KafkaConnectRollback:Streams:0:ShadowTopicName"] = Shadow,
        ["Messaging:KafkaConnectRollback:Streams:0:SourceDatabaseName"] = source, ["Messaging:KafkaConnectRollback:Streams:0:WorkerClientId"] = worker,
        ["Messaging:KafkaConnectRollback:Streams:0:ConsumerGroupId"] = Group,
    };
    private static async Task WaitAsync(Func<Task<bool>> predicate, string condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline) { if (await predicate()) return; await Task.Delay(500); }
        Assert.Fail("Timed out: " + condition);
    }
    private void Step(DatabaseOptions options, string step)
    {
        var line = DateTimeOffset.UtcNow.ToString("O") + " " + options.Provider + " " + step;
        TestContext.WriteLine(line);
        if (Environment.GetEnvironmentVariable("FULLNET_REAL_CONTROL_TRACE") is { Length: > 0 } trace) File.AppendAllText(trace, line + Environment.NewLine);
    }
    private sealed class PauseAuditFault
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public bool Armed { get; set; }
        public bool AuditFailed { get; set; }
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    /// <summary>只在生产证明返回后暂停测试推进，不制造或改写任何就绪字段。</summary>
    private sealed class PausedGateReader(KafkaConnectEventDeliveryRollbackReadinessReader inner, PauseAuditFault fault) : IEventDeliveryRollbackReadinessReader
    {
        public async Task<EventDeliveryRollbackReadiness> PrepareAsync(string type, int version, Guid generation, CancellationToken token = default)
        {
            var proof = await inner.PrepareAsync(type, version, generation, token);
            if (fault.Armed)
            {
                Assert.IsTrue(proof.ConnectorStopped && proof.BrokerMessagesDrainedOrIsolated && proof.SourcePositionCoversProducerFence);
                fault.Paused.TrySetResult(); await fault.Release.Task.WaitAsync(token);
            }
            return proof;
        }
        public Task AbortAsync(string type, int version, Guid generation, CancellationToken token = default) => inner.AbortAsync(type, version, generation, token);
    }
    private sealed class AuditFaultWriter(MessagingDomainAuditWriter inner, PauseAuditFault fault) : ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>
    {
        public Task WriteAsync(MessagingDomainAuditWrite entry, CancellationToken token = default)
        {
            if (fault.Armed && entry.ActionKey == MessagingDomainAuditActionKeys.DeliveryRollback && entry.Outcome == MessagingDomainAuditOutcomes.Success)
            {
                fault.Armed = false; fault.AuditFailed = true;
                throw new IOException("Simulated final audit failure after genuine Connect pause.");
            }
            return inner.WriteAsync(entry, token);
        }
    }
}
