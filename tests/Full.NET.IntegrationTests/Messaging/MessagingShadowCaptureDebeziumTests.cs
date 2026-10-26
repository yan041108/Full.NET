using System.Data.Common;
using System.Text;
using Confluent.Kafka;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Messaging.Features.ManageShadowCapture;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MsSql;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>独立影子表必须经真实 CDC 交付；观察消息不进入业务 Inbox。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MessagingShadowCaptureDebeziumTests
{
    [TestMethod]
    public async Task Shadow_connector_factory_resolves_topic_and_keeps_capture_metadata()
    {
        var config = await DebeziumConnectorTemplateFactory.CreateMySqlShadowConfigAsync(
            "Server=localhost;Database=fixture;User ID=fixture;Password=fixture", "host.docker.internal", "kafka:9092");
        Assert.IsFalse(config.Values.Any(value => value.Contains("${", StringComparison.Ordinal)),
            "提交 Connect 之前必须替换所有部署占位符。");
        Assert.IsTrue(MessagingNames.TopicCodePattern.IsMatch(config["transforms.outbox.route.topic.replacement"]),
            "受控影子目标必须符合生产证明使用的版本化 TopicCode。");
        StringAssert.EndsWith(config["table.include.list"], ".fn_messaging_shadow_event");
        StringAssert.Contains(config["transforms.outbox.table.fields.additional.placement"], "CaptureGeneration:header:capture_generation");
        var sql = await DebeziumConnectorTemplateFactory.CreateSqlServerShadowConfigAsync(
            "Server=localhost;Database=fixture;User ID=fixture;Password=fixture", "host.docker.internal", "kafka:9092");
        Assert.IsFalse(sql.Values.Any(value => value.Contains("${", StringComparison.Ordinal)));
        Assert.AreEqual("dbo.fn_messaging_shadow_event", sql["table.include.list"]);
        foreach (var formal in new[]
        {
            await DebeziumConnectorTemplateFactory.CreateMySqlShadowConfigAsync(
                "Server=localhost;Database=fixture;User ID=fixture;Password=fixture", "host.docker.internal", "kafka:9092", useFormalOutbox: true),
            await DebeziumConnectorTemplateFactory.CreateSqlServerShadowConfigAsync(
                "Server=localhost;Database=fixture;User ID=fixture;Password=fixture", "host.docker.internal", "kafka:9092", useFormalOutbox: true),
        })
        {
            StringAssert.EndsWith(formal["table.include.list"], ".fn_messaging_outbox_event");
            Assert.IsFalse(formal["transforms.outbox.table.fields.additional.placement"].Contains("CaptureGeneration", StringComparison.Ordinal));
            Assert.IsFalse(formal.Values.Any(value => value.Contains("${FULLNET_", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    public async Task MySql_controlled_capture_reaches_real_debezium_and_resumes_pending_samples() =>
        await VerifyAsync(new()
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
        });

    [TestMethod]
    public async Task SqlServer_agent_controlled_capture_reaches_real_debezium_and_resumes_pending_samples()
    {
        // Agent 仅在本场景独享实例启用，禁止重启或更改其他会话使用的共享 SQL Server。
        await using var server = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword(SharedDatabaseFixture.MySqlRootPassword)
            .WithEnvironment("MSSQL_AGENT_ENABLED", "true")
            .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048")
            .WithCreateParameterModifier(parameters =>
            {
                parameters.HostConfig ??= new();
                parameters.HostConfig.Memory = 3L << 30;
                parameters.HostConfig.MemorySwap = 3L << 30;
                parameters.HostConfig.NanoCPUs = 1_000_000_000L;
            }).Build();
        await server.StartAsync();
        var builder = new SqlConnectionStringBuilder(server.GetConnectionString());
        await using (var admin = new SqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync();
            builder.InitialCatalog = "fullnet_cdc_" + Guid.NewGuid().ToString("N");
            await admin.ExecuteAsync($"CREATE DATABASE [{builder.InitialCatalog}]");
        }
        await VerifyAsync(new() { Provider = DatabaseProvider.SqlServer, ConnectionString = builder.ConnectionString });
    }

    private static async Task VerifyAsync(DatabaseOptions options)
    {
        // 固定业务发生时间，刻意与 Connector 处理时间分开，识别时标配置或精度丢失。
        var clock = new CaptureClock(DateTimeOffset.UtcNow.AddMinutes(-1));
        await using var provider = await EventDeliveryPilotTestSupport.BuildPilotServicesAsync(options,
            seedCutoverEvidence: false, configureServices: services => services.AddSingleton<IClock>(clock));
        await using DbConnection database = options.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(options.ConnectionString) : new MySqlConnection(options.ConnectionString);
        await database.OpenAsync();
        if (options.Provider == DatabaseProvider.SqlServer)
        {
            await database.ExecuteAsync("EXEC sys.sp_cdc_enable_db");
            var root = FullNetApiFactory.FindRepositoryRoot();
            await database.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root, "deploy/messaging/sqlserver/enable-shadow-cdc.sql")));
        }
        else Assert.IsTrue((await CdcShadowFixture.ReadMySqlBinlogStatusAsync(options.ConnectionString)).IsRowFullEnabled);

        var pipeline = await CdcDebeziumPipelineFixture.GetOrStartAsync();
        using var connect = pipeline.CreateConnectAdminClient();
        Assert.IsTrue(await connect.WaitUntilReadyAsync(TimeSpan.FromSeconds(60)));
        var name = "fullnet-capture-" + Guid.NewGuid().ToString("N");
        var config = options.Provider == DatabaseProvider.SqlServer
            ? await DebeziumConnectorTemplateFactory.CreateSqlServerShadowConfigAsync(options.ConnectionString, pipeline.HostGateway, pipeline.InternalKafkaBootstrapServers)
            : await DebeziumConnectorTemplateFactory.CreateMySqlShadowConfigAsync(options.ConnectionString, pipeline.HostGateway, pipeline.InternalKafkaBootstrapServers);
        var topic = config["transforms.outbox.route.topic.replacement"];
        Assert.IsFalse(topic.Contains("${", StringComparison.Ordinal), "Topic 必须由部署占位符解析，不能在运行测试中另造路由。");
        try
        {
            await connect.RegisterConnectorAsync(name, CdcDebeziumConnectorTestSupport.CreateIsolatedConnectorConfig(name, config));
            Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(name, TimeSpan.FromSeconds(120)),
                "已注册的真实 Connector 必须健康，不能将产品配置失败记成环境跳过。");
            await WaitAsync(async () => await connect.TryReadConnectorPositionAsync(name) is not null,
                "Connector 必须完成 no_data 初始化并持久报告源位点。");
            await using var scope = EventDeliveryPilotTestSupport.CreateHostScope(provider);
            var capture = scope.ServiceProvider.GetRequiredService<ShadowCaptureService>();
            var type = EventDeliveryPilotTestSupport.PilotEventType;
            var start = await capture.StartAsync(new(Guid.CreateVersion7(), null, type, 1, 2, 300, "real CDC capture"), default);
            Assert.IsTrue(start.IsSuccess, start.Error?.Message);
            var generation = start.Value!.Generation;
            var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
            var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
            var tenant = Guid.CreateVersion7();
            var payload = new IdentityOrganizationUnitChangedIntegrationEvent(tenant, Guid.CreateVersion7(), "real CDC", true, 1, clock.UtcNow);
            var metadata = IntegrationEventMetadata.Create("capture-real-" + generation.ToString("N"), "fullnet.messaging.capture.e2e");
            async Task WriteAsync(bool rollback = false) => await transaction.ExecuteAsync(async token =>
            {
                await writer.AddAsync(type, 1, payload, metadata, token);
                if (rollback) throw new InvalidOperationException("capture transaction rollback");
                return 0;
            }, default);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => WriteAsync(rollback: true));
            Assert.AreEqual(0, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_event"));
            await WriteAsync();
            var first = await database.QuerySingleAsync<Sample>("SELECT * FROM fn_messaging_shadow_event");
            var observed = await CdcDebeziumE2ESupport.TryConsumeShadowEventAsync(pipeline, topic, first.Id, TimeSpan.FromSeconds(120));
            Assert.IsNotNull(observed, "真实提交的 Legacy 样本必须经独立影子 CDC 到达 Kafka。");
            VerifyEnvelope(first, observed);

            await connect.PauseConnectorAsync(name);
            await WaitAsync(() => connect.IsConnectorPausedAsync(name), "Connector 与全部任务必须确认暂停。");
            await WriteAsync();
            var samples = (await database.QueryAsync<Sample>("SELECT * FROM fn_messaging_shadow_event ORDER BY Id")).ToArray();
            var pending = samples.Single(sample => sample.Id != first.Id);
            Assert.IsNull(await CdcDebeziumE2ESupport.TryConsumeShadowEventAsync(pipeline, topic, pending.Id, TimeSpan.FromSeconds(4)),
                "完整暂停期间不得交付新样本。");
            await connect.ResumeConnectorAsync(name);
            Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(name, TimeSpan.FromSeconds(120)));
            var resumed = await CdcDebeziumE2ESupport.TryConsumeShadowEventAsync(pipeline, topic, pending.Id, TimeSpan.FromSeconds(120));
            Assert.IsNotNull(resumed, "恢复后必须交付暂停期间真实提交的样本。");
            VerifyEnvelope(pending, resumed);
            Assert.AreEqual("closed", (await capture.FindAsync(type, 1, default)).Value!.State);
            Assert.AreEqual(0, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event"));
            Assert.AreEqual(0, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_inbox_message"), "影子观察不得调用业务消费者。");
            var legacy = (await database.QueryAsync<(Guid Id, byte[] Payload)>("SELECT Id, Payload FROM fn_outbox_message")).ToDictionary(row => row.Id, row => row.Payload);
            Assert.AreEqual(2, legacy.Count);
            foreach (var sample in samples) CollectionAssert.AreEqual(legacy[sample.Id], sample.Payload);

            var kafkaOptions = CdcDebeziumE2ESupport.CreateKafkaOptions(pipeline, "fullnet.capture.verifier");
            var formal = IdentityIntegrationEventTopicDefinitions.OrganizationUnitChanged;
            var probe = new KafkaEventDeliveryCutoverProbe(Options.Create(new KafkaConnectRollbackOptions
            {
                Enabled = true, Streams = [new() { EventType = type, SchemaVersion = 1, TopicName = formal.TopicCode, ShadowTopicName = topic }],
            }), Options.Create(kafkaOptions), new CaptureEnvironment(),
                new IntegrationEventSubscriptionCatalog([formal], []), connect, new KafkaReplayConsumerFactory(Options.Create(kafkaOptions)));
            Assert.IsNotNull(await probe.CompareShadowAsync(type, 1, samples.Select(sample => sample.ToExpected()).ToArray()),
                "真实 Connector 输出必须满足生产证明的字节、代次、精确发生时间和固定窗口比较。");
        }
        finally { await connect.DeleteConnectorAsync(name); }
    }

    private static void VerifyEnvelope(Sample sample, ConsumeResult<string, byte[]> observed)
    {
        Assert.IsTrue(new KafkaEnvelopeReader().TryRead(observed, out var envelope, out var failure), failure);
        Assert.IsNotNull(envelope);
        Assert.AreEqual(sample.Id, envelope.EventId);
        Assert.AreEqual(sample.OccurredAtUtc, envelope.OccurredAtUtc);
        CollectionAssert.AreEqual(sample.Payload, envelope.Payload.ToArray());
        var generation = observed.Message.Headers.Single(header => header.Key == "capture_generation");
        Assert.IsTrue(KafkaEnvelopeHeaderParsers.TryParseGuidHeader(Encoding.UTF8.GetString(generation.GetValueBytes()), out var parsed));
        Assert.AreEqual(sample.CaptureGeneration, parsed);
        Assert.IsTrue(observed.Message.Timestamp.UnixTimestampMs >= sample.OccurredAtUtc.ToUnixTimeMilliseconds(),
            "时间定位不能把本代样本排除在起始位点之前。");
    }

    private static async Task WaitAsync(Func<Task<bool>> predicate, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(500);
        }
        Assert.Fail(message);
    }

    private sealed record CaptureClock(DateTimeOffset UtcNow) : IClock;
    private sealed class CaptureEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Messaging.Capture.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class Sample
    {
        public Guid Id { get; set; }
        public string PartitionKey { get; set; } = "";
        public byte[] Payload { get; set; } = [];
        public DateTimeOffset OccurredAtUtc { get; set; }
        public string ContentType { get; set; } = "";
        public Guid? TenantId { get; set; }
        public string? CorrelationId { get; set; }
        public Guid? CausationId { get; set; }
        public string? TraceParent { get; set; }
        public string Producer { get; set; } = "";
        public Guid CaptureGeneration { get; set; }
        public CutoverShadowEvent ToExpected() => new(Id, PartitionKey, Payload, OccurredAtUtc, ContentType,
            TenantId, CorrelationId, CausationId, TraceParent, Producer, CaptureGeneration);
    }
}
