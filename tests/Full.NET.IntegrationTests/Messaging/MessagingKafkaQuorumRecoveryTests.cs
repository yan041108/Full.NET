using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>三台独占 Broker 的单节点中断：验证正式 Producer 的副本确认与失败关闭，不外推物理故障域。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MessagingKafkaQuorumRecoveryTests
{
    [TestMethod]
    [TestCategory("RequiresDocker")]
    public async Task Three_brokers_preserve_delivery_with_two_ISR_and_refuse_three_ISR_until_recovered()
    {
        var evidence = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR")
            ?? throw new InvalidOperationException("三 Broker 验收必须经过正式资源入口。");
        var started = DateTimeOffset.UtcNow;
        var ports = Enumerable.Range(0, 3).Select(_ => AvailablePort()).ToArray();
        await using var network = new NetworkBuilder().Build();
        await network.CreateAsync();
        var quorum = string.Join(',', Enumerable.Range(1, 3).Select(i => $"{i}@broker{i}:9093"));
        var brokers = Enumerable.Range(1, 3).Select(i => new ContainerBuilder("apache/kafka:4.1.2")
            .WithNetwork(network).WithNetworkAliases($"broker{i}")
            .WithPortBinding(ports[i - 1], 9094)
            .WithEnvironment("KAFKA_HEAP_OPTS", "-Xms256M -Xmx512M")
            .WithEnvironment("KAFKA_NODE_ID", i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_LISTENERS", "PLAINTEXT://:9092,EXTERNAL://:9094,CONTROLLER://:9093")
            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", $"PLAINTEXT://broker{i}:9092,EXTERNAL://127.0.0.1:{ports[i - 1]}")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "PLAINTEXT:PLAINTEXT,EXTERNAL:PLAINTEXT,CONTROLLER:PLAINTEXT")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", quorum)
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "3")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "3")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "2")
            .WithEnvironment("KAFKA_REPLICA_LAG_TIME_MAX_MS", "5000")
            .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "false")
            .WithEnvironment("CLUSTER_ID", "MkU3OEVBNTcwNTJENDM2Qk")
            .WithCreateParameterModifier(p =>
            {
                var limits = p.HostConfig ??= new(); limits.Memory = 1536L << 20;
                limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
            })
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"))
            .Build()).ToArray();
        try
        {
            // KRaft 首次选举需要多数节点同时启动；所有资源均由本轮持有并在 finally 中回收。
            await Task.WhenAll(brokers.Select(broker => broker.StartAsync()));
            var options = new KafkaMessagingOptions
            {
                Enabled = true, BootstrapServers = string.Join(',', ports.Select(port => $"127.0.0.1:{port}")),
                ClientId = "fullnet.messaging.quorum.acceptance", DeliveryTimeoutMilliseconds = 15_000,
            };
            using var admin = new AdminClientBuilder(options.BuildClientConfig()).Build();
            var cluster = await admin.DescribeClusterAsync(new() { RequestTimeout = TimeSpan.FromSeconds(10) });
            Assert.AreEqual(3, cluster.Nodes.Count);
            var suffix = Guid.NewGuid().ToString("N");
            var available = "fullnet.dev.messaging.quorum.two." + suffix;
            var strict = "fullnet.dev.messaging.quorum.three." + suffix;
            await admin.CreateTopicsAsync([Topic(available, "2"), Topic(strict, "3")],
                new() { RequestTimeout = TimeSpan.FromSeconds(30), OperationTimeout = TimeSpan.FromSeconds(30) });
            await WaitAsync(() => HasIsr(available, 3) && HasIsr(strict, 3), "初始三个同步副本");
            using var producer = new KafkaMessagingProducer(Options.Create(options));
            Assert.IsTrue(await SendAsync(strict, "before"));
            for (var i = 0; i < 10; i++) Assert.IsTrue(await SendAsync(available, "before-" + i));
            var leader = admin.GetMetadata(strict, TimeSpan.FromSeconds(5)).Topics.Single().Partitions.Single().Leader;
            var stopped = Enumerable.Range(1, 3).First(id => id != leader);
            var interrupted = DateTimeOffset.UtcNow;
            await brokers[stopped - 1].PauseAsync();
            bool refused;
            try
            {
                await WaitAsync(() => HasIsr(available, 2) && HasIsr(strict, 2), "单节点中断后真实 ISR 降至二");
                for (var i = 0; i < 10; i++) Assert.IsTrue(await SendAsync(available, "during-" + i), "RF=3/minISR=2 应继续确认。");
                refused = !await SendAsync(strict, "must-refuse");
                Assert.IsTrue(refused, "RF=3/minISR=3 在副本不足时不能返回成功。");
            }
            finally { await brokers[stopped - 1].UnpauseAsync(); }
            await WaitAsync(() => HasIsr(available, 3) && HasIsr(strict, 3), "恢复三个同步副本");
            Assert.IsTrue(await SendAsync(strict, "after"));
            var recovered = DateTimeOffset.UtcNow;
            var availableKeys = ReadAll(available, 20);
            var strictKeys = ReadAll(strict, 2);
            Assert.AreEqual(20, availableKeys.Distinct(StringComparer.Ordinal).Count());
            CollectionAssert.AreEquivalent(new[] { "before", "after" }, strictKeys.ToArray());
            await File.WriteAllTextAsync(Path.Combine(evidence, "three-broker-recovery.json"), JsonSerializer.Serialize(new
            {
                StartedAtUtc = started, InterruptedAtUtc = interrupted, RecoveredAtUtc = recovered,
                RecoverySeconds = (recovered - interrupted).TotalSeconds, BrokerCount = 3, ReplicationFactor = 3,
                SinglePausedBrokerId = stopped, AvailableTopicAcknowledgedAndConsumed = availableKeys.Count,
                StrictTopicAcknowledgedAndConsumed = strictKeys.Count, InsufficientReplicasRefused = refused,
                CpuPerBroker = 1, MemoryMiBPerBroker = 1536, Producer = "KafkaMessagingProducer (idempotent/Acks.All)",
                Scope = "Local Docker single Broker pause; no independent physical fault-domain or throughput certification",
                CapacityStatus = "Capacity-not-verified",
            }));

            TopicSpecification Topic(string name, string minimum) => new()
            {
                Name = name, NumPartitions = 1, ReplicationFactor = 3,
                Configs = new() { ["min.insync.replicas"] = minimum, ["unclean.leader.election.enable"] = "false" },
            };
            bool HasIsr(string topic, int count)
            {
                try
                {
                    var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(5)).Topics.Single();
                    return !metadata.Error.IsError && metadata.Partitions.Count == 1 && metadata.Partitions[0].InSyncReplicas.Length == count;
                }
                catch (KafkaException exception) when (exception.Error.Code is ErrorCode.Local_TimedOut
                    or ErrorCode.Local_Transport or ErrorCode.NetworkException or ErrorCode.RequestTimedOut)
                {
                    // 节点暂停后，既有 Admin 连接可能先超时；只在原两分钟期限内等待实际 ISR，不视为已收敛。
                    return false;
                }
            }
            Task<bool> SendAsync(string topic, string key) => producer.TryProduceAsync(topic,
                new() { Key = key, Value = new byte[256] }, default);
            List<string> ReadAll(string topic, int expected)
            {
                using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig(options.BuildClientConfig())
                {
                    GroupId = "fullnet.dev.messaging.quorum." + Guid.NewGuid().ToString("N"), EnableAutoCommit = false,
                }).Build();
                consumer.Assign(new TopicPartitionOffset(topic, 0, Offset.Beginning));
                var keys = new List<string>();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (keys.Count < expected) keys.Add(consumer.Consume(timeout.Token).Message.Key);
                var end = consumer.QueryWatermarkOffsets(new(topic, 0), TimeSpan.FromSeconds(5)).High.Value;
                Assert.AreEqual((long)expected, end, "Broker 实际尾部必须与已确认消息一致，不能遗留未知结果。");
                consumer.Close(); return keys;
            }
        }
        finally
        {
            foreach (var broker in brokers) await broker.DisposeAsync();
        }
    }

    private static ushort AvailablePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        return checked((ushort)((IPEndPoint)socket.LocalEndpoint).Port);
    }

    private static async Task WaitAsync(Func<bool> condition, string message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { while (!condition()) await Task.Delay(1000, timeout.Token); }
        catch (OperationCanceledException) { Assert.Fail("等待失败：" + message); }
    }
}
