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

[TestClass]
[DoNotParallelize]
public sealed class KafkaOutboxCdcCapacityRunnerTests
{
    [TestMethod]
    [TestCategory("RequiresDocker")]
    [DataRow(DatabaseProvider.MySql)]
    [DataRow(DatabaseProvider.SqlServer)]
    public async Task Scope_C_runs_outbox_cdc_inbox_pipeline(DatabaseProvider provider)
    {
        var scenario = provider == DatabaseProvider.MySql
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
            Path.GetTempPath(),
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
            var exitCode = await KafkaCapacityRunner.RunCommandAsync([
                "--settings", settings,
                "--scope", KafkaCapacityScopeCodes.TransactionOutboxCdc,
                "--host-parity-mode", "worker",
                "--execute", "true",
                "--approval-id", "integration-test",
                "--reason", "scope-c-real-pipeline",
                "--run-id", $"scope-c-{Guid.NewGuid():N}",
                "--output", output,
                "--scenarios", "low-rate",
                "--low-rates", "20",
                "--payload-sizes", "128",
                "--producer-concurrency", "2",
                "--partitions", "2",
                "--replication-factor", "1",
                "--warmup-seconds", "0",
                "--duration-seconds", "2",
                "--drain-seconds", "60",
                "--max-messages-per-sample", "100",
            ]);

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
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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

    private sealed class ScopeCScenario(
        CdcDebeziumPipelineEnvironment pipeline,
        KafkaConnectAdminClient connectAdmin,
        DatabaseOptions options,
        string connectionString,
        string databaseName) : IAsyncDisposable
    {
        public CdcDebeziumPipelineEnvironment Pipeline { get; } = pipeline;

        public DatabaseOptions Options { get; } = options;

        public string ConnectionString { get; } = connectionString;

        public string DatabaseName { get; } = databaseName;

        public async ValueTask DisposeAsync()
        {
            connectAdmin.Dispose();
            await ValueTask.CompletedTask;
        }
    }
}
