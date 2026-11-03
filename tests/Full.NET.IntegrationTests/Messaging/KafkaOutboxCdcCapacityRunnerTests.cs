extern alias kafkabenchmarks;

using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Messaging.Kafka;
using Full.NET.IntegrationTests.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using KafkaCapacityConfiguration = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityConfiguration;
using KafkaCapacityExitCode = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityExitCode;
using KafkaCapacityOptions = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityOptions;
using KafkaCapacityRunner = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityRunner;
using KafkaCapacitySampleEvidence = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacitySampleEvidence;
using KafkaCapacitySampleState = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacitySampleState;
using KafkaCapacityScopeCodes = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityScopeCodes;
using KafkaCapacityWorkerContracts = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityWorkerContracts;
using KafkaCapacityConnectorTemplateFactory = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityConnectorTemplateFactory;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>集成测试：KafkaOutboxCdcCapacityRunner。</summary>
[TestClass]
[DoNotParallelize]
public sealed partial class KafkaOutboxCdcCapacityRunnerTests
{
    [TestMethod]
    [TestCategory("RequiresDocker")]
    [DataRow(DatabaseProvider.MySql)]
    [DataRow(DatabaseProvider.SqlServer)]
    public async Task Scope_C_runs_outbox_cdc_inbox_pipeline(DatabaseProvider provider)
    {
        var soak = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_SOAK_SECONDS") == "3600";
        var owned = soak || Environment.GetEnvironmentVariable("FULLNET_MESSAGING_OWNED_CAPACITY") == "1";
        var scenario = owned ? await CreateOwnedSoakScenarioAsync(provider) : provider == DatabaseProvider.MySql
            ? await TryCreateMySqlScenarioAsync()
            : await TryCreateSqlServerScenarioAsync();
        if (scenario is null)
        {
            Assert.Inconclusive(
                "Scope C integration prerequisites are unavailable for "
                + provider);
        }

        await using var scope = scenario;
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = scope.Pipeline.BootstrapServers,
        }).Build();
        var cluster = await admin.DescribeClusterAsync(
            new DescribeClusterOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
        var root = Path.Combine(
            owned ? Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR")
                ?? throw new InvalidOperationException("有界 Soak 必须由正式入口提供任务证据目录。") : Path.GetTempPath(),
            $"fullnet-kafka-capacity-scope-c-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settings = Path.Combine(root, "settings.json");
        var output = Path.Combine(root, "evidence");
        await File.WriteAllTextAsync(
            settings,
            JsonSerializer.Serialize(new
            {
                KafkaCapacity = new
                {
                    ExecutionEnabled = true,
                    EnvironmentName = "Capacity",
                    ExpectedClusterId = cluster.ClusterId,
                    Kafka = CdcDebeziumE2ESupport.CreateKafkaOptions(
                        scope.Pipeline,
                        "capacity-scope-c-it"),
                    Database = new
                    {
                        Provider = scope.Options.Provider.ToString(),
                        ConnectionString = scope.ConnectionString,
                        ExpectedDatabaseName = scope.DatabaseName,
                        CommandTimeoutSeconds = 60,
                        MySqlGuidStorageMode = "Binary16",
                    },
                    Connect = new
                    {
                        BaseUri = scope.Pipeline.ConnectBaseUri.ToString(),
                        RequestTimeoutSeconds = 60,
                        HealthTimeoutSeconds = 120,
                        ConnectorNamePrefix = "fullnet-capacity-it",
                        DatabaseHostGateway = scope.Pipeline.HostGateway,
                        InternalKafkaBootstrapServers = scope.Pipeline.InternalKafkaBootstrapServers,
                        MySqlConnectorUser = "root",
                        MySqlConnectorPassword = SharedDatabaseFixture.MySqlRootPassword,
                    },
                },
            }));
        try
        {
            using var backlog = owned ? new MessagingSoakBacklogRecorder(scope.Options, KafkaCapacityWorkerContracts.EventType,
                KafkaCapacityWorkerContracts.ConsumerName, Path.Combine(root, "database-backlog.ndjson")) : null;
            async Task SampleBacklogAsync(CancellationToken token) { if (backlog is not null) await backlog.CaptureAsync(token); }
            await using var resources = soak ? new MessagingSoakResourceRecorder(scope.ResourceIds, Path.Combine(root, "resources.ndjson"), SampleBacklogAsync) : null;
            if (soak) await File.WriteAllTextAsync(Path.Combine(root, "soak-input.json"), JsonSerializer.Serialize(new
            {
                Provider = provider.ToString(), DurationSeconds = 3600, RatePerSecond = 2, PayloadBytes = 256,
                WorkerPipelineInstances = 1, Partitions = 2, ReplicationFactor = 1,
                DatabaseMemoryGiB = provider == DatabaseProvider.SqlServer ? 3d : 1.5d, KafkaMemoryGiB = 2, ConnectMemoryGiB = 2,
                CpuPerContainer = 1, DrainSeconds = 180, MaximumMessages = 7200,
                BrokerPauseAfterSeconds = 1800, BrokerPauseSeconds = 10,
                ThresholdSource = "Development acceptance: Runner integrity/no unknown outcomes; no production latency budget",
                StopConditions = "3600s arrivals + 180s drain; reject integrity/unknown outcomes or missing resource evidence; 130m harness limit for both databases",
                CapacityStatus = "Capacity-not-verified", Scope = "transaction_outbox_cdc / WorkerParity",
            }));
            using var faultLifetime = new CancellationTokenSource();
            using var loadLifetime = new CancellationTokenSource();
            var fault = soak ? InterruptOwnedSoakBrokerAsync(scope.Pipeline, root, faultLifetime.Token, SampleBacklogAsync) : Task.CompletedTask;
            var load = KafkaCapacityRunner.RunCommandAsync([
                "--settings", settings,
                "--scope", KafkaCapacityScopeCodes.TransactionOutboxCdc,
                "--host-parity-mode", "worker",
                "--execute", "true",
                "--approval-id", soak ? "messaging-reliability-20261028" : "integration-test",
                "--reason", soak ? "owned-dual-database-bounded-soak" : "scope-c-real-pipeline",
                "--run-id", $"scope-c-{Guid.NewGuid():N}",
                "--output", output,
                "--scenarios", "low-rate",
                "--low-rates", soak ? "2" : "20",
                "--payload-sizes", soak ? "256" : "128",
                "--producer-concurrency", soak ? "1" : "2",
                "--partitions", "2",
                "--replication-factor", "1",
                "--warmup-seconds", "0",
                "--duration-seconds", soak ? "3600" : "2",
                "--drain-seconds", soak ? "180" : "60",
                "--max-messages-per-sample", soak ? "7200" : "100",
            ], loadLifetime.Token);
            KafkaCapacityExitCode exitCode;
            try
            {
                if (resources is not null && await Task.WhenAny(load, resources.Completion) != load)
                {
                    // 资源记录提前退出即停止负载；不能等一小时后才发现峰值证据已经丢失。
                    await loadLifetime.CancelAsync();
                    await load;
                    await resources.Completion;
                    Assert.Fail("长跑资源采样提前结束。");
                }
                exitCode = await load;
            }
            finally { await faultLifetime.CancelAsync(); await fault; }

            if (exitCode != KafkaCapacityExitCode.Success && Directory.Exists(output))
            {
                var samplePath = Path.Combine(output, "samples.ndjson");
                if (File.Exists(samplePath))
                {
                    Assert.Fail(
                        "Scope C runner failed: "
                        + File.ReadAllText(samplePath));
                }
            }

            Assert.AreEqual(KafkaCapacityExitCode.Success, exitCode);
            var sample = JsonSerializer.Deserialize<KafkaCapacitySampleEvidence>(
                File.ReadAllLines(Path.Combine(output, "samples.ndjson")).Single(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    Converters =
                    {
                        new System.Text.Json.Serialization.JsonStringEnumConverter(),
                    },
                })!;
            Assert.AreEqual(KafkaCapacityScopeCodes.TransactionOutboxCdc, sample.ScopeCode);
            Assert.IsTrue(sample.Integrity.CorrectnessPassed);
            Assert.IsNotNull(sample.OutboxCdc);
            Assert.AreEqual(sample.Integrity.Acknowledged, sample.OutboxCdc!.CdcPublished);
            Assert.AreEqual(sample.Integrity.Consumed, sample.OutboxCdc.CdcPublished);
            Assert.IsGreaterThan(0, sample.Integrity.Consumed);
            if (soak) Assert.AreEqual(7200L, sample.Integrity.Consumed, "有界长跑必须实际对账全部计划消息。");
            if (owned)
            {
                var persisted = await backlog!.CaptureAsync();
                Assert.AreEqual(sample.Integrity.Consumed, persisted.OutboxSamples, "独享库正式负载 Outbox 必须逐项入账。");
                Assert.AreEqual(sample.Integrity.Consumed, persisted.ProcessedInboxSamples, "不能只凭 Handler 内存计数冒充 Inbox 已提交。");
                Assert.AreEqual(0L, persisted.PendingSamples);
            }
            if (soak) Assert.IsTrue(File.Exists(Path.Combine(root, "broker-pause.json")), "必须实际完成已登记的 Broker 中断恢复。");
        }
        finally
        {
            // 长跑保留原始报告和资源采样，始终删除只用于本轮连接的凭据文件。
            if (owned) File.Delete(settings);
            else Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ScopeCScenario?> TryCreateMySqlScenarioAsync()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 60,
        };
        await MigrateAndSeedOwnershipAsync(options.Provider, connectionString);
        var binlogStatus = await CdcShadowFixture.ReadMySqlBinlogStatusAsync(connectionString);
        if (!binlogStatus.IsRowFullEnabled)
        {
            return null;
        }

        var pipeline = await CdcDebeziumPipelineFixture.GetOrStartAsync();
        var connectAdmin = pipeline.CreateConnectAdminClient();
        if (!await connectAdmin.WaitUntilReadyAsync(TimeSpan.FromSeconds(60)))
        {
            return null;
        }

        return new ScopeCScenario(
            pipeline,
            connectAdmin,
            options,
            connectionString,
            new MySqlConnectionStringBuilder(connectionString).Database!);
    }

    private static async Task<ScopeCScenario?> TryCreateSqlServerScenarioAsync()
    {
        var connectionString = await SqlServerCdcTestSupport.ResolveConnectionStringAsync();
        var cdcEnablement = await SqlServerCdcTestSupport.TryEnableCdcAsync(connectionString);
        if (!cdcEnablement.Succeeded)
        {
            return null;
        }

        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = connectionString,
            CommandTimeoutSeconds = 60,
        };
        await MigrateAndSeedOwnershipAsync(options.Provider, connectionString);
        var pipeline = await CdcDebeziumPipelineFixture.GetOrStartAsync();
        var connectAdmin = pipeline.CreateConnectAdminClient();
        if (!await connectAdmin.WaitUntilReadyAsync(TimeSpan.FromSeconds(60)))
        {
            return null;
        }

        return new ScopeCScenario(
            pipeline,
            connectAdmin,
            options,
            connectionString,
            new SqlConnectionStringBuilder(connectionString).InitialCatalog);
    }

    private static async Task MigrateAndSeedOwnershipAsync(
        DatabaseProvider provider,
        string connectionString)
    {
        var options = Options.Create(new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = provider == DatabaseProvider.MySql
                ? MySqlGuidStorageMode.Binary16
                : MySqlGuidStorageMode.LegacyChar36,
            CommandTimeoutSeconds = 60,
        });
        var migration = new DbUpMigrationRunner(
            options,
            NullLoggerFactory.Instance,
            MigrationContractOptionFactory.UuidOptions(),
            MigrationContractOptionFactory.NamingOptions());
        Assert.IsTrue((await migration.MigrateAsync()).Successful);
        var parameters = new
        {
            MessageType = KafkaCapacityWorkerContracts.EventType,
            SchemaVersion = KafkaCapacityWorkerContracts.SchemaVersion,
            TopicCode = KafkaCapacityWorkerContracts.TopicCode,
        };
        if (provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.ExecuteAsync(
                """
                IF NOT EXISTS (
                    SELECT 1 FROM dbo.fn_messaging_stream_ownership
                    WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion)
                INSERT INTO dbo.fn_messaging_stream_ownership
                    (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                     CutoffEventId, CutoffOccurredAtUtc, Reason, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@MessageType, @SchemaVersion, @TopicCode, 2, 0,
                     '00000000-0000-0000-0000-000000000000', SYSDATETIMEOFFSET(),
                     N'Scope C capacity integration test', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                """,
                parameters);
            return;
        }

        await using var mySql = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await mySql.ExecuteAsync(
            """
            INSERT INTO fn_messaging_stream_ownership
                (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                 CutoffEventId, CutoffOccurredAtUtc, Reason, CreatedAtUtc, UpdatedAtUtc)
            SELECT @MessageType, @SchemaVersion, @TopicCode, 2, 0,
                   0x00000000000000000000000000000000, UTC_TIMESTAMP(6),
                   'Scope C capacity integration test', UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
            FROM DUAL
            WHERE NOT EXISTS (
                SELECT 1 FROM fn_messaging_stream_ownership
                WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion);
            """,
            parameters);
    }

    internal sealed class ScopeCScenario(
        CdcDebeziumPipelineEnvironment pipeline,
        KafkaConnectAdminClient connectAdmin,
        DatabaseOptions options,
        string connectionString,
        string databaseName, IAsyncDisposable? ownedDatabase = null, IReadOnlyList<string>? resourceIds = null) : IAsyncDisposable
    {
        public CdcDebeziumPipelineEnvironment Pipeline { get; } = pipeline;

        public DatabaseOptions Options { get; } = options;

        public string ConnectionString { get; } = connectionString;

        public string DatabaseName { get; } = databaseName;
        public IReadOnlyList<string> ResourceIds { get; } = resourceIds ?? [];

        public async ValueTask DisposeAsync()
        {
            connectAdmin.Dispose();
            if (ownedDatabase is not null)
            {
                try { await Pipeline.DisposeAsync(); }
                finally { await ownedDatabase.DisposeAsync(); }
            }
        }
    }
}
