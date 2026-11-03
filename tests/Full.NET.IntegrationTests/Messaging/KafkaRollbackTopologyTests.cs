using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Full.NET.Messaging.Kafka;
using Testcontainers.Kafka;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>真实 Broker 同名重建不能被误判为空队列排空；正式与重试资源使用同一守卫。</summary>
[TestClass]
[DoNotParallelize]
public sealed class KafkaRollbackTopologyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Same_name_recreation_with_missing_group_offset_cannot_satisfy_the_frozen_topology(bool retry)
    {
        await using var broker = new KafkaBuilder("apache/kafka:4.1.2").WithEnvironment("KAFKA_HEAP_OPTS", "-Xms256m -Xmx512m")
            .WithEnvironment("KAFKA_LOG_CLEANUP_POLICY", "compact")
            .WithEnvironment("KAFKA_AUTHORIZER_CLASS_NAME", "org.apache.kafka.metadata.authorizer.StandardAuthorizer")
            .WithEnvironment("KAFKA_ALLOW_EVERYONE_IF_NO_ACL_FOUND", "true")
            .WithCreateParameterModifier(p =>
            {
                var limits = p.HostConfig ??= new();
                limits.Memory = 1536L << 20; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
            }).Build();
        await broker.StartAsync();
        var options = new KafkaMessagingOptions { Enabled = true, BootstrapServers = broker.GetBootstrapAddress(), SecurityProtocol = "Plaintext" };
        using var admin = new AdminClientBuilder(options.BuildClientConfig()).Build();
        var formal = "rollback-topology-" + Guid.NewGuid().ToString("N");
        var topic = retry ? KafkaTopicNames.GetRetryTopic(formal, "5s") : formal;
        var inherited = topic + "-inherited";
        var mixed = topic + "-mixed";
        var unreadable = topic + "-config-denied";
        await admin.CreateTopicsAsync([
            new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1, Configs = new Dictionary<string, string> { ["cleanup.policy"] = "delete" } },
            new TopicSpecification { Name = inherited, NumPartitions = 1, ReplicationFactor = 1 },
            new TopicSpecification { Name = mixed, NumPartitions = 1, ReplicationFactor = 1, Configs = new Dictionary<string, string> { ["cleanup.policy"] = "compact,delete" } },
            new TopicSpecification { Name = unreadable, NumPartitions = 1, ReplicationFactor = 1, Configs = new Dictionary<string, string> { ["cleanup.policy"] = "delete" } },
        ]);
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = options.BootstrapServers, MessageTimeoutMs = 10000 }).Build();
        await producer.ProduceAsync(topic, new Message<string, string> { Key = "unconfirmed", Value = "must-not-be-forgotten" });
        var group = "rollback-topology-group-" + Guid.NewGuid().ToString("N");
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig { BootstrapServers = options.BootstrapServers, GroupId = group, EnableAutoCommit = false }).Build();
        var partition = new TopicPartition(topic, 0);
        consumer.Assign(partition); consumer.Commit([new TopicPartitionOffset(partition, 0)]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var observer = new KafkaConsumerLagObserver();
        Assert.IsNull(await observer.ObserveTopicBindingsAsync(options, [inherited], deadline.Token),
            "必须读取继承自 Broker 的有效策略，不能把未显式配置的 compact Topic 当作逐事件日志。");
        Assert.IsNull(await observer.ObserveTopicBindingsAsync(options, [mixed], deadline.Token), "compact,delete 仍可能删除内部事件。");
        var readableBaseline = await observer.ObserveTopicBindingsAsync(options, [unreadable], deadline.Token);
        Assert.IsNotNull(readableBaseline);
        // 只拒绝本例 Topic 的配置读取，仍显式允许元数据描述，避免把不存在 Topic 误作权限测试。
        AclBinding Acl(AclOperation operation, AclPermissionType permission) => new()
        {
            Pattern = new() { Type = ResourceType.Topic, Name = unreadable, ResourcePatternType = ResourcePatternType.Literal },
            Entry = new() { Principal = "User:ANONYMOUS", Host = "*", Operation = operation, PermissionType = permission },
        };
        var denied = Acl(AclOperation.DescribeConfigs, AclPermissionType.Deny);
        await admin.CreateAclsAsync([Acl(AclOperation.Describe, AclPermissionType.Allow),
            Acl(AclOperation.DescribeConfigs, AclPermissionType.Allow), denied], new CreateAclsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
        await WaitForConfigPermissionAsync(false);
        Assert.IsFalse((await admin.DescribeTopicsAsync(TopicCollection.OfTopicNames([unreadable]))).TopicDescriptions.Single().Error.IsError);
        Assert.IsNull(await observer.ObserveTopicBindingsAsync(options, [unreadable], deadline.Token));
        Assert.IsFalse(await observer.IsDrainedAsync(options, readableBaseline[0], group, deadline.Token),
            "配置读取拒绝时，即使 Topic 当前为空也不能认定安全排空。");
        await admin.DeleteAclsAsync([denied.ToFilter()], new DeleteAclsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
        await WaitForConfigPermissionAsync(true);
        Assert.IsNotNull(await observer.ObserveTopicBindingsAsync(options, [unreadable], deadline.Token));
        var frozen = await observer.ObserveTopicBindingsAsync(options, [topic], deadline.Token);
        Assert.IsNotNull(frozen); Assert.AreEqual(1L, frozen[0].Partitions.Single().High);
        Assert.IsFalse(await observer.IsDrainedAsync(options, frozen[0], group, deadline.Token));
        await admin.DeleteRecordsAsync([new TopicPartitionOffset(partition, 1)]);
        consumer.Commit([new TopicPartitionOffset(partition, 1)]);
        var truncated = await observer.ObserveTopicBindingsAsync(options, [topic], deadline.Token);
        Assert.IsNotNull(truncated); Assert.AreEqual(frozen[0].TopicId, truncated[0].TopicId);
        Assert.AreEqual(1L, truncated[0].Partitions.Single().Low);
        Assert.IsFalse(KafkaDeliveryTopology.Matches(frozen, truncated), "即使消费组已跳到当前 high，也不能掩盖自切流以来的保留窗口裁剪。");
        Assert.IsFalse(await observer.IsDrainedAsync(options, frozen[0], group, deadline.Token));
        await admin.DeleteTopicsAsync([topic]);
        while (true)
        {
            try
            {
                await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1,
                    Configs = new Dictionary<string, string> { ["cleanup.policy"] = "delete" } }]);
                break;
            }
            catch (CreateTopicsException ex) when (ex.Results.All(item => item.Error.Code == ErrorCode.TopicAlreadyExists))
            { await Task.Delay(100, deadline.Token); }
        }
        var recreated = await observer.ObserveTopicBindingsAsync(options, [topic], deadline.Token);
        Assert.IsNotNull(recreated); Assert.AreEqual(0L, recreated[0].Partitions.Single().High);
        Assert.AreNotEqual(frozen[0].TopicId, recreated[0].TopicId);
        Assert.IsFalse(KafkaDeliveryTopology.Matches(frozen, recreated));
        Assert.IsFalse(await observer.IsDrainedAsync(options, frozen[0], group, deadline.Token), "新空 Topic 不能替代旧 Topic 的业务确认。");
        consumer.Close();

        async Task WaitForConfigPermissionAsync(bool allowed)
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    await admin.DescribeConfigsAsync([new ConfigResource { Type = ResourceType.Topic, Name = unreadable }],
                        new DescribeConfigsOptions { RequestTimeout = TimeSpan.FromSeconds(2) });
                    if (allowed) return;
                }
                catch (DescribeConfigsException exception) when (exception.Results.Count == 1
                    && exception.Results.All(result => result.Error.Code == ErrorCode.TopicAuthorizationFailed))
                {
                    if (!allowed) return;
                }
                await Task.Delay(100, deadline.Token);
            }
        }
    }
}
