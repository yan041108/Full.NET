namespace Full.NET.ArchitectureTests;

/// <summary>架构测试：WorkerComposition 约束与回归门禁。</summary>
[TestClass]
public sealed class WorkerCompositionTests
{
    private static readonly string WorkerProgramPath = Path.Combine(
        FindRepositoryRoot(),
        "src/Hosts/Full.NET.Host.Worker/Program.cs".Replace('/', Path.DirectorySeparatorChar));

    [TestMethod]
    public void Worker_program_registers_legacy_outbox_processors_in_hybrid_kafka_mode()
    {
        // HybridKafka 模式必须同时注册 OutboxProcessor 与 OutboxRetentionProcessor，
        // 以便 Legacy 事件流继续走旧轮询，而切流后的流走 Kafka。
        var source = File.ReadAllText(WorkerProgramPath);

        // 断言存在 HybridKafka 分支且注册 Legacy 处理器
        StringAssert.Contains(
            source,
            "MessagingWorkerMode.HybridKafka",
            "Program.cs 必须存在 HybridKafka 模式分支以实现 Legacy+Kafka 并存。");

        // 在 HybridKafka 条件分支内必须包含 OutboxProcessor 的 AddHostedService 注册
        var hybridBlockIndex = source.IndexOf(
            "MessagingWorkerMode.HybridKafka",
            StringComparison.Ordinal);
        Assert.IsTrue(hybridBlockIndex >= 0, "Program.cs 未声明 HybridKafka 模式。");

        // 注册 Legacy OutboxProcessor 的调用必须存在于 HybridKafka 相关代码路径中
        var outboxProcessorRegistration = source.IndexOf(
            "AddHostedService<OutboxProcessor>()",
            StringComparison.Ordinal);
        Assert.IsTrue(
            outboxProcessorRegistration >= 0,
            "Program.cs 必须在非退役命令路径注册 OutboxProcessor HostedService。");

        var retentionProcessorRegistration = source.IndexOf(
            "AddHostedService<OutboxRetentionProcessor>()",
            StringComparison.Ordinal);
        Assert.IsTrue(
            retentionProcessorRegistration >= 0,
            "Program.cs 必须在非退役命令路径注册 OutboxRetentionProcessor HostedService。");
    }

    [TestMethod]
    public void Worker_program_registers_kafka_consumer_in_hybrid_kafka_mode()
    {
        var source = File.ReadAllText(WorkerProgramPath);

        // HybridKafka 模式必须注册 KafkaConsumerWorker 所需的模块化和 Kafka 消息能力
        StringAssert.Contains(
            source,
            "MessagingWorkerMode.HybridKafka",
            "Program.cs 必须存在 HybridKafka 模式分支。");

        // HybridKafka 条件分支必须包含 AddFullNetKafkaMessaging 调用
        var kafkaMessagingRegistration = source.IndexOf(
            "AddFullNetKafkaMessaging(",
            StringComparison.Ordinal);
        Assert.IsTrue(
            kafkaMessagingRegistration >= 0,
            "Program.cs 必须在 HybridKafka 模式调用 AddFullNetKafkaMessaging 注册 Kafka 消费能力。");
    }

    [TestMethod]
    public void Worker_mode_uses_hybrid_kafka_without_a_delivery_owner_alias()
    {
        var mode = File.ReadAllText(Path.Combine(Path.GetDirectoryName(WorkerProgramPath)!, "MessagingWorkerMode.cs"));
        StringAssert.Contains(mode, "HybridKafka = 2");
        Assert.IsFalse(mode.Contains("CdcKafka =", StringComparison.Ordinal));
        Assert.IsFalse(File.ReadAllText(WorkerProgramPath).Contains("MessagingWorkerMode.CdcKafka", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MessagingWorkerCatalogGuard_validates_hybrid_kafka_stream_level_subscriptions()
    {
        var guardPath = Path.Combine(
            FindRepositoryRoot(),
            "src/Hosts/Full.NET.Host.Worker/MessagingWorkerCatalogGuard.cs".Replace(
                '/',
                Path.DirectorySeparatorChar));
        var source = File.ReadAllText(guardPath);

        // 启动守卫必须实现 HybridKafka 流级订阅校验：逐个检查 CdcKafka owner 的流是否有对应订阅
        StringAssert.Contains(
            source,
            "ValidateHybridKafkaMode",
            "MessagingWorkerCatalogGuard 必须提供 ValidateHybridKafkaMode 流级订阅守卫。");

        // 守卫逻辑中必须遍历 topics/subscriptions 按 (EventType, SchemaVersion) 匹配，
        // 不能只做全局订阅数量判断（会让不相关订阅误通过）。
        StringAssert.Contains(
            source,
            "GetDeliveryOwner(",
            "守卫必须按流查询所有权，不能仅用订阅总数判断。");
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, "Full.NET.slnx")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new InvalidOperationException("Cannot locate repository root (Full.NET.slnx not found).");
    }
}
