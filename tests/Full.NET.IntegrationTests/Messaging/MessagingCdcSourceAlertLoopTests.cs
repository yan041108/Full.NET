using System.Data.Common;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>源窗口监控专项：只制造独享诊断事件和实际日志清理，不代替业务切流/恢复证据。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MessagingCdcSourceAlertLoopTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.MySql)]
    [DataRow(DatabaseProvider.SqlServer)]
    public async Task Actual_connector_position_loss_fires_and_a_new_isolated_baseline_resolves_alert(DatabaseProvider provider)
    {
        await using var scope = await KafkaOutboxCdcCapacityRunnerTests.CreateOwnedSoakScenarioAsync(provider);
        var pipeline = scope.Pipeline;
        var topic = "fullnet.dev.shadow.health." + Guid.NewGuid().ToString("N");
        var group = "fullnet.dev.source.health." + Guid.NewGuid().ToString("N");
        var connector = "fullnet-source-health-" + Guid.NewGuid().ToString("N")[..12];
        var kafka = CdcDebeziumE2ESupport.CreateKafkaOptions(pipeline, "fullnet.source.health");
        using var admin = new AdminClientBuilder(kafka.BuildClientConfig()).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        using var connect = pipeline.CreateConnectAdminClient();
        var config = await MessagingCutoverRollbackDebeziumTests.ConnectorAsync(scope.Options, pipeline, useFormal: true);
        config["transforms.outbox.route.topic.replacement"] = topic;
        config["schema.history.internal.kafka.topic"] += "." + connector;
        await connect.RegisterConnectorAsync(connector, CdcDebeziumConnectorTestSupport.CreateIsolatedConnectorConfig(connector, config));
        Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(connector, TimeSpan.FromSeconds(120)));
        await WaitAsync(async () => await connect.TryReadConnectorPositionAsync(connector) is not null, "初始真实源坐标");
        var committed = await CdcShadowFixture.InsertCommittedOutboxEventAsync(scope.Options, "health-before-" + Guid.NewGuid().ToString("N"));
        var consumerConfig = kafka.BuildConsumerConfig(group); consumerConfig.GroupInstanceId = null;
        using (var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig).Build())
        {
            consumer.Subscribe(topic);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var observed = consumer.Consume(deadline.Token);
            Assert.IsTrue(new Full.NET.Messaging.Kafka.KafkaEnvelopeReader().TryRead(observed, out var envelope, out _));
            Assert.AreEqual(committed.Fingerprint.EventId, envelope!.EventId);
            // 这是本用例新建的诊断组；真实提交便于观测零 lag，不接触任何正式消费组。
            consumer.Commit(observed); consumer.Close();
        }
        await connect.PauseConnectorAsync(connector);
        await WaitAsync(() => connect.IsConnectorPausedAsync(connector), "诊断 Connector 实际暂停");
        // 推进源高水位，确保 SQL Server 实际 cleanup 可越过旧提交；没有修改业务流所有权。
        await CdcShadowFixture.InsertCommittedOutboxEventAsync(scope.Options, "health-after-" + Guid.NewGuid().ToString("N"));
        await using DbConnection database = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(scope.ConnectionString) : new MySqlConnection(scope.ConnectionString);
        await database.OpenAsync();
        await MessagingCdcSourceAlertVerification.VerifyAsync(scope.Options, database, pipeline, scope.DatabaseName,
            connector, topic, group, kafka, MessagingOutboxTestSupport.TestEventType);
        await connect.DeleteConnectorAsync(connector);
    }

    private static async Task WaitAsync(Func<Task<bool>> condition, string description)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { while (!await condition()) await Task.Delay(500, timeout.Token); }
        catch (OperationCanceledException) { Assert.Fail("等待失败：" + description); }
    }
}
