using System.Data.Common;
using Confluent.Kafka;
using Dapper;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    /// <summary>验证同名捕获实例被其它表占用时，运维脚本必须失败关闭。</summary>
    private static async Task VerifyHeartbeatCaptureMismatchAsync(string serverConnection)
    {
        var connection = new SqlConnectionStringBuilder(serverConnection);
        var database = "fullnet_heartbeat_mismatch_" + Guid.CreateVersion7().ToString("N");
        await using (var admin = new SqlConnection(connection.ConnectionString))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE DATABASE [{database}]");
        }
        connection.InitialCatalog = database;
        await using var db = new SqlConnection(connection.ConnectionString);
        await db.OpenAsync();
        await WaitKafkaAsync(async () => await db.ExecuteScalarAsync<string?>("SELECT TOP (1) status_desc FROM sys.dm_server_services WHERE servicename LIKE N'SQL Server Agent (%'") == "Running", "SQL Server Agent 已启动");
        await db.ExecuteAsync("EXEC sys.sp_cdc_enable_db");
        var root = FullNetApiFactory.FindRepositoryRoot();
        var migration = await File.ReadAllTextAsync(Path.Combine(root, "src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/352_MessagingCdcHeartbeat.sql"));
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(migration, @"(?im)^\s*GO\s*$").Where(batch => !string.IsNullOrWhiteSpace(batch)))
            await db.ExecuteAsync(batch);
        await db.ExecuteAsync("CREATE TABLE dbo.fn_test_heartbeat_decoy (Id uniqueidentifier NOT NULL PRIMARY KEY, UpdatedAtUtc datetime2(6) NOT NULL)");
        await db.ExecuteAsync("EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'fn_test_heartbeat_decoy', @role_name=NULL, @capture_instance=N'fullnet_fn_messaging_cdc_heartbeat', @supports_net_changes=0");
        var script = await File.ReadAllTextAsync(Path.Combine(root, "deploy/messaging/sqlserver/enable-heartbeat-cdc.sql"));
        var error = await Assert.ThrowsAsync<SqlException>(async () => await db.ExecuteAsync(script));
        StringAssert.Contains(error.Message, "CDC 心跳捕获实例已绑定其他表");
        Assert.AreEqual("fn_test_heartbeat_decoy", await db.ExecuteScalarAsync<string>("SELECT OBJECT_NAME(source_object_id) FROM cdc.change_tables WHERE capture_instance=N'fullnet_fn_messaging_cdc_heartbeat'"));
    }

    /// <summary>没有业务捕获活动时，仅由目标 Connector 的受控心跳事务推动持久位点。</summary>
    private static async Task VerifyAutonomousIdleHeartbeatAsync(DbConnection db, CdcDebeziumPipelineEnvironment pipeline,
        KafkaConnectAdminClient connect, string connector, string prefix, IEnumerable<string> allConnectors)
    {
        foreach (var other in allConnectors.Where(name => name != connector))
        {
            await connect.PauseConnectorAsync(other);
            await WaitKafkaAsync(() => connect.IsConnectorPausedAsync(other), "其它 Connector 已暂停，不能提供心跳事务");
        }
        var configuration = await connect.TryReadConnectorConfigurationAsync(connector);
        Assert.IsNotNull(configuration);
        Assert.AreEqual("UPDATE dbo.fn_messaging_cdc_heartbeat SET UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = '01941f29-7c00-7000-8000-000000000001'", configuration["heartbeat.action.query"]);
        Assert.IsFalse(configuration["table.include.list"].Contains("fn_messaging_cdc_heartbeat", StringComparison.OrdinalIgnoreCase), "基础设施心跳不得进入业务流。");
        var captures = (await db.QueryAsync<string>("SELECT capture_instance FROM cdc.change_tables ORDER BY capture_instance")).ToArray();
        CollectionAssert.AreEquivalent(new[] { "fullnet_fn_messaging_cdc_heartbeat", "fullnet_fn_messaging_outbox_event", "fullnet_fn_messaging_shadow_event" }, captures);
        var outboxCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event");
        var shadowCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_event");
        var capturedCount = await db.ExecuteScalarAsync<int>("SELECT (SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_outbox_event_CT) + (SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_shadow_event_CT)");
        var previousUpdate = await db.ExecuteScalarAsync<DateTime>("SELECT UpdatedAtUtc FROM fn_messaging_cdc_heartbeat");
        CdcDeliveryPosition? baseline = null;
        await WaitKafkaAsync(async () => (baseline = await connect.TryReadConnectorPositionAsync(connector))?.Lsn is not null, "目标 Connector 基线位点");
        CdcDeliveryPosition? heartbeatFence = null;
        await WaitKafkaAsync(async () =>
        {
            var lsn = await db.ExecuteScalarAsync<byte[]>("SELECT MAX(__$start_lsn) FROM cdc.fullnet_fn_messaging_cdc_heartbeat_CT WHERE __$operation=4 AND UpdatedAtUtc>@PreviousUpdate", new { PreviousUpdate=previousUpdate });
            if (lsn is not { Length: 10 }) return false;
            var candidate = CdcDeliveryPosition.ForSqlServerBytes(null, lsn);
            if (!CdcDeliveryPosition.ConnectorCoversProducerFence(baseline!, candidate)) return false;
            heartbeatFence = candidate;
            return true;
        }, "Connector 自动心跳产生新的已捕获事务");
        var heartbeatTopic = configuration["topic.heartbeat.prefix"] + "." + configuration["topic.prefix"];
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig { BootstrapServers=pipeline.BootstrapServers,
            GroupId=prefix + ".autonomous-idle", EnableAutoCommit=false }).Build();
        var partition = new TopicPartition(heartbeatTopic, new Partition(0));
        consumer.Assign(new TopicPartitionOffset(partition, consumer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10)).High));
        Assert.IsNotNull(await Task.Run(() => consumer.Consume(TimeSpan.FromSeconds(45))), "必须观察到新的实际 Broker 心跳。");
        CdcDeliveryPosition? observed = null;
        await WaitKafkaAsync(async () =>
        {
            observed = await connect.TryReadConnectorPositionAsync(connector);
            return observed?.Lsn is not null && (string.Equals(observed.Lsn.CommitLsn, heartbeatFence!.Lsn!.CommitLsn, StringComparison.OrdinalIgnoreCase)
                || CdcDeliveryPosition.ConnectorCoversProducerFence(heartbeatFence!, observed));
        }, "持久 Connector 位点覆盖自身心跳事务");
        Assert.AreEqual(outboxCount, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event"));
        Assert.AreEqual(shadowCount, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_event"));
        Assert.AreEqual(capturedCount, await db.ExecuteScalarAsync<int>("SELECT (SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_outbox_event_CT) + (SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_shadow_event_CT)"), "业务捕获实例必须保持空闲。");
        Console.WriteLine("SQL Server autonomous heartbeat LSN advanced: " + baseline!.Lsn!.CommitLsn + " -> " + observed!.Lsn!.CommitLsn);
    }

    /// <summary>目标捕获实例空闲时，以独立捕获实例事务验证实际心跳和Connector偏移推进。</summary>
    private static async Task VerifyIdleHeartbeatPositionAsync(DbConnection db, CdcDebeziumPipelineEnvironment pipeline,
        KafkaConnectAdminClient connect, string connector, string prefix)
    {
        var configuration = await connect.TryReadConnectorConfigurationAsync(connector);
        Assert.IsNotNull(configuration);
        Assert.IsFalse(configuration["table.include.list"].Contains("fn_test_workflow_idle_probe", StringComparison.OrdinalIgnoreCase),
            "目标Connector不得捕获本例专用测试表。");
        var heartbeatTopic = configuration["topic.heartbeat.prefix"] + "." + configuration["topic.prefix"];
        using var heartbeat = new ConsumerBuilder<string, byte[]>(new ConsumerConfig { BootstrapServers = pipeline.BootstrapServers,
            GroupId = prefix + ".idle-offset", EnableAutoCommit = false }).Build();
        var partition = new TopicPartition(heartbeatTopic, new Partition(0));
        heartbeat.Assign(new TopicPartitionOffset(partition, heartbeat.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10)).High));
        await db.ExecuteAsync("CREATE TABLE dbo.fn_test_workflow_idle_probe (Id int NOT NULL PRIMARY KEY)");
        await db.ExecuteAsync("""
            EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'fn_test_workflow_idle_probe',
                @role_name=NULL, @capture_instance=N'fullnet_test_workflow_idle_probe', @supports_net_changes=0
            """);
        await db.ExecuteAsync("INSERT INTO dbo.fn_test_workflow_idle_probe (Id) VALUES (0)");
        CdcDeliveryPosition? setupFence = null;
        await WaitKafkaAsync(async () =>
        {
            var lsn = await db.ExecuteScalarAsync<byte[]>("SELECT TOP (1) __$start_lsn FROM cdc.fullnet_test_workflow_idle_probe_CT WHERE Id=0 AND __$operation=2");
            if (lsn is not { Length: 10 }) return false;
            setupFence = CdcDeliveryPosition.ForSqlServerBytes(null, lsn);
            return true;
        }, "独立CDC实例建立真实基线");
        CdcDeliveryPosition? baseline = null;
        await WaitKafkaAsync(async () =>
        {
            baseline = await connect.TryReadConnectorPositionAsync(connector);
            return baseline?.Lsn is not null && (string.Equals(baseline.Lsn.CommitLsn, setupFence!.Lsn!.CommitLsn, StringComparison.OrdinalIgnoreCase)
                || CdcDeliveryPosition.ConnectorCoversProducerFence(setupFence!, baseline));
        }, "目标Connector追上独立实例基线");
        var outboxCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event");
        var shadowCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_event");
        var cdcCount = await db.ExecuteScalarAsync<int>("SELECT (SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_outbox_event_CT) + (SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_shadow_event_CT)");
        await db.ExecuteAsync("INSERT INTO dbo.fn_test_workflow_idle_probe (Id) VALUES (1)");
        CdcDeliveryPosition? sourceFence = null;
        await WaitKafkaAsync(async () =>
        {
            var lsn = await db.ExecuteScalarAsync<byte[]>("SELECT TOP (1) __$start_lsn FROM cdc.fullnet_test_workflow_idle_probe_CT WHERE Id=1 AND __$operation=2");
            if (lsn is not { Length: 10 }) return false;
            var candidate = CdcDeliveryPosition.ForSqlServerBytes(null, lsn);
            if (!CdcDeliveryPosition.ConnectorCoversProducerFence(baseline!, candidate)) return false;
            sourceFence = candidate;
            return true;
        }, "独立CDC实例的真实事务越过目标流基线");
        // 只接收源事务已被CDC捕获之后新产生的心跳。
        heartbeat.Assign(new TopicPartitionOffset(partition, heartbeat.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10)).High));
        var record = await Task.Run(() => heartbeat.Consume(TimeSpan.FromSeconds(45)));
        Assert.IsNotNull(record, "目标流空闲时必须有真实Broker心跳。");
        CdcDeliveryPosition? observed = null;
        await WaitKafkaAsync(async () =>
        {
            observed = await connect.TryReadConnectorPositionAsync(connector);
            return observed?.Lsn is not null && (string.Equals(observed.Lsn.CommitLsn, sourceFence!.Lsn!.CommitLsn, StringComparison.OrdinalIgnoreCase)
                || CdcDeliveryPosition.ConnectorCoversProducerFence(sourceFence!, observed));
        }, "目标捕获流无新行时Connector LSN追上独立实例事务");
        Assert.AreEqual(outboxCount, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event"));
        Assert.AreEqual(shadowCount, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_event"));
        Assert.AreEqual(cdcCount, await db.ExecuteScalarAsync<int>("SELECT (SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_outbox_event_CT) + (SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_shadow_event_CT)"),
            "目标捕获实例不得有新行。");
        Console.WriteLine("SQL Server idle target heartbeat LSN advanced: " + baseline!.Lsn!.CommitLsn + " -> " + observed!.Lsn!.CommitLsn);
    }
}
