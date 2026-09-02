extern alias kafkabenchmarks;

using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.IntegrationTests.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using ConfluentKafkaCapacityAdminClient = kafkabenchmarks::Full.NET.Benchmarks.Kafka.ConfluentKafkaCapacityAdminClient;
using ConfluentKafkaCapacityConsumerFactory = kafkabenchmarks::Full.NET.Benchmarks.Kafka.ConfluentKafkaCapacityConsumerFactory;
using ConfluentKafkaCapacityProducerFactory = kafkabenchmarks::Full.NET.Benchmarks.Kafka.ConfluentKafkaCapacityProducerFactory;
using KafkaCapacityControlPlaneException = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityControlPlaneException;
using KafkaCapacityExitCode = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityExitCode;
using KafkaCapacityFingerprint = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityFingerprint;
using KafkaCapacityOptions = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityOptions;
using KafkaCapacityRunner = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityRunner;
using KafkaCapacitySampleContext = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacitySampleContext;
using KafkaCapacitySampleEvidence = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacitySampleEvidence;
using KafkaCapacitySampleState = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacitySampleState;
using KafkaCapacityScenarioCatalog = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityScenarioCatalog;
using KafkaCapacityTopicManager = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityTopicManager;
using KafkaCapacityTopicDescription = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityTopicDescription;
using KafkaCapacityTransportExecutor = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityTransportExecutor;
using KafkaCapacityScopeCodes = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityScopeCodes;
using KafkaCapacityWorkerContracts = kafkabenchmarks::Full.NET.Benchmarks.Kafka.KafkaCapacityWorkerContracts;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class KafkaCapacityRunnerTests
{
    [TestMethod]
    [TestCategory("RequiresDocker")]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Scope_B_uses_real_Kafka_worker_Inbox_and_handler(
        DatabaseProvider provider)
    {
        var kafka = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await MigrateAndSeedScopeBOwnershipAsync(provider, connectionString);
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = kafka.BootstrapServers,
        }).Build();
        var cluster = await admin.DescribeClusterAsync(
            new DescribeClusterOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
        var root = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-kafka-capacity-scope-b-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settings = Path.Combine(root, "settings.json");
        var output = Path.Combine(root, "evidence");
        var databaseName = provider == DatabaseProvider.SqlServer
            ? new SqlConnectionStringBuilder(connectionString).InitialCatalog
            : new MySqlConnectionStringBuilder(connectionString).Database;
        await File.WriteAllTextAsync(
            settings,
            JsonSerializer.Serialize(new
            {
                KafkaCapacity = new
                {
                    ExecutionEnabled = true,
                    EnvironmentName = "Capacity",
                    ExpectedClusterId = cluster.ClusterId,
                    Kafka = kafka.CreateOptions("capacity-scope-b-it"),
                    Database = new
                    {
                        Provider = provider.ToString(),
                        ConnectionString = connectionString,
                        ExpectedDatabaseName = databaseName,
                        CommandTimeoutSeconds = 30,
                        MySqlGuidStorageMode = "Binary16",
                    },
                },
            }));
        try
        {
            var exitCode = await KafkaCapacityRunner.RunCommandAsync([
                "--settings", settings,
                "--scope", KafkaCapacityScopeCodes.WorkerInboxHandler,
                "--execute", "true",
                "--approval-id", "integration-test",
                "--reason", "scope-b-real-pipeline",
                "--run-id", $"scope-b-{Guid.NewGuid():N}",
                "--output", output,
                "--scenarios", "low-rate",
                "--low-rates", "20",
                "--payload-sizes", "128",
                "--producer-concurrency", "2",
                "--partitions", "2",
                "--replication-factor", "1",
                "--warmup-seconds", "0",
                "--duration-seconds", "2",
                "--drain-seconds", "20",
                "--max-messages-per-sample", "100",
            ]);

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
            Assert.AreEqual(KafkaCapacityScopeCodes.WorkerInboxHandler, sample.ScopeCode);
            Assert.IsTrue(sample.Integrity.CorrectnessPassed);
            Assert.IsGreaterThan(0, sample.Integrity.Consumed);
            Assert.AreEqual(sample.Integrity.Acknowledged, sample.Integrity.Consumed);
            Assert.AreEqual(
                sample.Integrity.Consumed,
                sample.Performance.EndToEndLatency.Count);
            Assert.AreEqual(
                sample.Integrity.Acknowledged,
                sample.Performance.AcknowledgementLatency.Count);
            Assert.IsGreaterThan(0, sample.Performance.ScheduleLatency.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 验证真实 Kafka 的低速率与吞吐场景正确，且运行 Topic 在样本结束后保留。
    /// </summary>
    /// <returns>表示异步容量验证的任务。</returns>
    [TestMethod]
    [TestCategory("RequiresDocker")]
    public async Task Real_Kafka_low_rate_and_throughput_are_correct_and_topic_is_retained()
    {
        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = environment.BootstrapServers,
        }).Build();
        var cluster = await admin.DescribeClusterAsync(
            new DescribeClusterOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
        var root = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-kafka-capacity-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settings = Path.Combine(root, "settings.json");
        var output = Path.Combine(root, "evidence");
        var runId = $"it-{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(
            settings,
            JsonSerializer.Serialize(new
            {
                KafkaCapacity = new
                {
                    ExecutionEnabled = true,
                    EnvironmentName = "Capacity",
                    ExpectedClusterId = cluster.ClusterId,
                    Kafka = environment.CreateOptions("capacity-it"),
                },
            }));
        try
        {
            var exitCode = await KafkaCapacityRunner.RunCommandAsync([
                "--settings", settings,
                "--execute", "true",
                "--approval-id", "integration-test",
                "--reason", "real-kafka-verification",
                "--run-id", runId,
                "--output", output,
                "--scenarios", "low-rate,throughput",
                "--low-rates", "20",
                "--throughput-rates", "200",
                "--payload-sizes", "128",
                "--producer-concurrency", "2",
                "--partitions", "2",
                "--replication-factor", "1",
                "--warmup-seconds", "0",
                "--duration-seconds", "2",
                "--drain-seconds", "15",
                "--max-messages-per-sample", "1000",
            ]);

            var samples = File.ReadAllLines(Path.Combine(output, "samples.ndjson"))
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<KafkaCapacitySampleEvidence>(
                    line,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        Converters =
                        {
                            new System.Text.Json.Serialization.JsonStringEnumConverter(),
                        },
                })!)
                .ToArray();
            Assert.AreEqual(
                KafkaCapacityExitCode.Success,
                exitCode,
                JsonSerializer.Serialize(samples));
            Assert.HasCount(2, samples);
            Assert.IsTrue(samples.All(static sample =>
                sample.State == KafkaCapacitySampleState.Completed
                && sample.Integrity.CorrectnessPassed
                && sample.Integrity.Acknowledged == sample.Integrity.Consumed));

            var topicName = $"fullnet.capacity.{runId}.v1";
            var description = await admin.DescribeTopicsAsync(
                TopicCollection.OfTopicNames([topicName]),
                new DescribeTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
            Assert.AreEqual(topicName, description.TopicDescriptions.Single().Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("RequiresDocker")]
    public async Task Real_Kafka_explicit_delete_revalidates_TopicId()
    {
        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var options = environment.CreateOptions("capacity-delete-it");
        using var admin = new AdminClientBuilder(options.BuildClientConfig()).Build();
        var adapter = new ConfluentKafkaCapacityAdminClient(
            admin,
            TimeSpan.FromSeconds(10));
        var cluster = await adapter.DescribeClusterAsync(CancellationToken.None);
        var manager = new KafkaCapacityTopicManager(adapter);
        var identity = await manager.EnsureTopicAsync(
            $"delete-{Guid.NewGuid():N}",
            KafkaCapacityFingerprint.Sha256(cluster.ClusterId),
            2,
            1,
            null,
            CancellationToken.None);

        try
        {
            Assert.IsTrue(await manager.DeleteOwnedTopicAsync(
                identity,
                deleteRequested: true,
                CancellationToken.None));
            await WaitUntilAsync(
                async () => await adapter.DescribeTopicAsync(
                    identity.TopicName,
                    CancellationToken.None) is null,
                TimeSpan.FromSeconds(15));
            await adapter.CreateTopicAsync(
                identity.TopicName,
                identity.Partitions,
                identity.ReplicationFactor,
                CancellationToken.None);
            var replacement = await WaitForTopicAsync(
                adapter,
                identity.TopicName,
                TimeSpan.FromSeconds(15));
            Assert.AreNotEqual(identity.TopicId, replacement.TopicId);

            var exception = await Assert.ThrowsExactlyAsync<KafkaCapacityControlPlaneException>(() =>
                manager.DeleteOwnedTopicAsync(
                    identity,
                    deleteRequested: true,
                    CancellationToken.None));
            Assert.AreEqual("topic_identity_changed", exception.ReasonCode);
            Assert.IsNotNull(await adapter.DescribeTopicAsync(
                identity.TopicName,
                CancellationToken.None));
        }
        finally
        {
            if (await adapter.DescribeTopicAsync(
                    identity.TopicName,
                    CancellationToken.None) is not null)
            {
                await adapter.DeleteTopicAsync(
                    identity.TopicName,
                    CancellationToken.None);
            }
        }
    }

    [TestMethod]
    [TestCategory("RequiresDocker")]
    public async Task Real_Kafka_cancellation_returns_incomplete_evidence()
    {
        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var runId = $"cancel-{Guid.NewGuid():N}";
        using var admin = new AdminClientBuilder(
            environment.CreateOptions("capacity-cancel-it").BuildClientConfig()).Build();
        var adapter = new ConfluentKafkaCapacityAdminClient(
            admin,
            TimeSpan.FromSeconds(10));
        var cluster = await adapter.DescribeClusterAsync(CancellationToken.None);
        var manager = new KafkaCapacityTopicManager(adapter);
        var topic = await manager.EnsureTopicAsync(
            runId,
            KafkaCapacityFingerprint.Sha256(cluster.ClusterId),
            2,
            1,
            null,
            CancellationToken.None);
        var kafka = environment.CreateOptions("capacity-cancel-it");
        var executor = new KafkaCapacityTransportExecutor(
            kafka,
            new ConfluentKafkaCapacityProducerFactory(),
            new ConfluentKafkaCapacityConsumerFactory());
        var sample = KafkaCapacityScenarioCatalog.Build(
            KafkaCapacityOptions.Parse([
                "--scenarios", "throughput",
                "--throughput-rates", "200",
                "--payload-sizes", "128",
                "--producer-concurrency", "2",
                "--duration-seconds", "30",
                "--max-messages-per-sample", "10000",
            ]))[0];
        var context = KafkaCapacitySampleContext.Create(
            sample,
            topic,
            runId,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(15),
            10_000);
        var bypassGroup = $"capacity-bypass-{Guid.NewGuid():N}";
        var bypassPartition = new TopicPartition(
            topic.TopicName,
            new Partition(0));
        Offset bypassOffset;
        using (var bypass = environment.CreateConsumer(
                   bypassGroup,
                   "capacity-bypass-before"))
        {
            bypass.Assign(new TopicPartitionOffset(
                bypassPartition,
                Offset.Beginning));
            bypass.Commit([
                new TopicPartitionOffset(bypassPartition, new Offset(0)),
            ]);
            bypassOffset = bypass.Committed(
                [bypassPartition],
                TimeSpan.FromSeconds(10)).Single().Offset;
        }
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(2));

        var evidence = await executor.ExecuteAsync(context, cancellation.Token);

        Assert.AreEqual(KafkaCapacitySampleState.Incomplete, evidence.State);
        CollectionAssert.Contains(evidence.FailureCodes.ToArray(), "cancelled");
        using var bypassAfter = environment.CreateConsumer(
            bypassGroup,
            "capacity-bypass-after");
        var committedAfter = bypassAfter.Committed(
            [bypassPartition],
            TimeSpan.FromSeconds(10)).Single().Offset;
        Assert.AreEqual(bypassOffset, committedAfter);
    }

    private static async Task<KafkaCapacityTopicDescription> WaitForTopicAsync(
        ConfluentKafkaCapacityAdminClient adapter,
        string topicName,
        TimeSpan timeout)
    {
        KafkaCapacityTopicDescription? topic = null;
        await WaitUntilAsync(
            async () => (topic = await adapter.DescribeTopicAsync(
                topicName,
                CancellationToken.None)) is not null,
            timeout);
        return topic!;
    }

    private static async Task MigrateAndSeedScopeBOwnershipAsync(
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
                INSERT INTO dbo.fn_messaging_stream_ownership
                    (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                     CutoffEventId, CutoffOccurredAtUtc, Reason, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@MessageType, @SchemaVersion, @TopicCode, 2, 0,
                     '00000000-0000-0000-0000-000000000000', SYSDATETIMEOFFSET(),
                     N'Scope B capacity integration test', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
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
            VALUES
                (@MessageType, @SchemaVersion, @TopicCode, 2, 0,
                 0x00000000000000000000000000000000, UTC_TIMESTAMP(6),
                 'Scope B capacity integration test', UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
            """,
            parameters);
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.Fail("Kafka condition did not converge before the timeout.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
