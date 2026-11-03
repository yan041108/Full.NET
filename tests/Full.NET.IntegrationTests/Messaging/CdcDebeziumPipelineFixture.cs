using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Full.NET.IntegrationTests.Api;
using Full.NET.Messaging.Kafka;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// 固定版本 Kafka 4.1.2 + Debezium Connect 3.4.3.Final 测试栈。
/// 与 <see cref="KafkaFixture"/> 分离，避免现有 Kafka 测试被 Connect 网络拓扑影响。
/// </summary>
public sealed class CdcDebeziumPipelineEnvironment : IAsyncDisposable
{
    private const string KafkaImage = "apache/kafka:4.1.2";
    private const string DebeziumImage = "quay.io/debezium/connect:3.4.3.Final";
    private const string KafkaAlias = "kafka";
    private const ushort KafkaInternalPort = 9092;
    private const ushort KafkaExternalPort = 9094;
    private const ushort KafkaContainerClientPort = 9095;

    private readonly INetwork _network;
    private readonly IContainer _kafka;
    private readonly IContainer _connect;

    private CdcDebeziumPipelineEnvironment(
        INetwork network,
        IContainer kafka,
        IContainer connect,
        string bootstrapServers,
        Uri connectBaseUri,
        string hostGateway,
        string internalKafkaBootstrapServers)
    {
        _network = network;
        _kafka = kafka;
        _connect = connect;
        BootstrapServers = bootstrapServers;
        ConnectBaseUri = connectBaseUri;
        HostGateway = hostGateway;
        InternalKafkaBootstrapServers = internalKafkaBootstrapServers;
    }

    public string BootstrapServers { get; }

    public Uri ConnectBaseUri { get; }

    /// <summary>供 Connect 容器访问宿主机映射数据库端口的网关主机名。</summary>
    public string HostGateway { get; }

    /// <summary>Connect 容器内访问 Kafka 的 bootstrap（schema history 等）。</summary>
    public string InternalKafkaBootstrapServers { get; }

    /// <summary>本测试环境独占的实际 Broker 映射端口，仅供回环故障代理转发。</summary>
    internal ushort KafkaMappedPort { get; private init; }
    internal string ContainerClientBootstrapServers { get; private init; } = "";
    internal IReadOnlyList<string> OwnedContainerIds => [_kafka.Id, _connect.Id];

    /// <summary>创建独立CDC链路；紧凑档只用于小样本功能验收，不能作为容量证据。</summary>
    /// <param name="compactResources">将每个组件限制为1GiB、最大堆512MiB，为堆外及插件扫描保留余量；默认使用标准档。</param>
    public static async Task<CdcDebeziumPipelineEnvironment> StartAsync(ushort? advertisedHostPort = null, bool dockerReachableProxy = false, bool automaticTtl = false,
        bool compactResources = false, ushort? connectProxyPort = null, Action<ushort>? onKafkaStarted = null)
    {
        if (connectProxyPort is not null && (!dockerReachableProxy || advertisedHostPort != connectProxyPort || onKafkaStarted is null))
            throw new ArgumentException("Connect 故障代理必须使用该代理的容器可达监听地址。");
        var filterJar = Environment.GetEnvironmentVariable("FULLNET_KAFKA_STREAM_FILTER_JAR");
        var filterHash = Environment.GetEnvironmentVariable("FULLNET_KAFKA_STREAM_FILTER_SHA256");
        if (!string.IsNullOrEmpty(filterJar))
        {
            if (!Path.IsPathFullyQualified(filterJar) || !File.Exists(filterJar) ||
                (File.GetAttributes(filterJar) & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(filterJar))), filterHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Kafka流过滤插件路径或摘要无效。");
        }
        else if (!string.IsNullOrEmpty(filterHash)) throw new InvalidOperationException("Kafka流过滤插件路径缺失。");
        var networkBuilder = new NetworkBuilder();
        if (SharedDatabaseFixture.BoundedContainers)
        {
            // 外层会话也拥有网络，进程中断时不能只回收容器而遗留桥接资源。
            var labels = new Dictionary<string, string>();
            SharedDatabaseFixture.ApplyBoundedOwnership(labels);
            foreach (var (key, value) in labels) networkBuilder = networkBuilder.WithLabel(key, value);
        }
        var network = networkBuilder.Build();
        await network.CreateAsync().ConfigureAwait(false);
        var kafkaExternalHostPort = AllocateAvailableHostPort();
        var containerHostPort = dockerReachableProxy ? AllocateAvailableHostPort() : kafkaExternalHostPort;
        // Windows 观察端保留回环；容器走独立 Listener 和同一真实 Broker，避免依赖 Windows 的网关别名解析。
        var observerPort = dockerReachableProxy ? kafkaExternalHostPort : advertisedHostPort ?? kafkaExternalHostPort;
        var containerListener = dockerReachableProxy ? $",NATIVE://0.0.0.0:{KafkaContainerClientPort}" : "";
        var containerAdvertised = dockerReachableProxy ? $",NATIVE://host.docker.internal:{advertisedHostPort ?? containerHostPort}" : "";

        var kafkaBuilder = new ContainerBuilder(KafkaImage)
            .WithCreateParameterModifier(parameters =>
            {
                SharedDatabaseFixture.ApplyMessagingContainerResources(parameters, "kafka");
                if (compactResources) { parameters.HostConfig!.Memory = 1L << 30; parameters.HostConfig.MemorySwap = parameters.HostConfig.Memory; }
            })
            .WithLabel("fullnet.cdc.resource-profile", compactResources ? "functional-compact" : "standard")
            .WithEnvironment("KAFKA_HEAP_OPTS", "-Xms256M -Xmx512M")
            .WithNetwork(network)
            .WithNetworkAliases(KafkaAlias)
            .WithPortBinding(kafkaExternalHostPort, KafkaExternalPort)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment(
                "KAFKA_LISTENERS",
                $"PLAINTEXT://0.0.0.0:{KafkaInternalPort},EXTERNAL://0.0.0.0:{KafkaExternalPort},CONTROLLER://0.0.0.0:9093{containerListener}")
            .WithEnvironment(
                "KAFKA_ADVERTISED_LISTENERS",
                $"PLAINTEXT://{KafkaAlias}:{KafkaInternalPort},EXTERNAL://127.0.0.1:{observerPort}{containerAdvertised}")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment(
                "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP",
                "CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT,EXTERNAL:PLAINTEXT" + (dockerReachableProxy ? ",NATIVE:PLAINTEXT" : ""))
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", $"1@{KafkaAlias}:9093")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
            .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "true")
            .WithEnvironment("CLUSTER_ID", "fullnet-cdc-e2e-kafka-cluster")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Kafka Server started"));
        if (dockerReachableProxy) kafkaBuilder = kafkaBuilder.WithPortBinding(containerHostPort, KafkaContainerClientPort);
        // 仅独立 TTL 验收加速真实后台清理，普通场景保留镜像默认周期。
        if (automaticTtl) kafkaBuilder = kafkaBuilder.WithEnvironment("KAFKA_LOG_RETENTION_CHECK_INTERVAL_MS", "1000");
        var kafka = kafkaBuilder.Build();

        var javaCryptoPolicy = ResolveJavaCryptoPolicyPath();
        var connectBuilder = new ContainerBuilder(DebeziumImage)
            .WithCreateParameterModifier(parameters =>
            {
                SharedDatabaseFixture.ApplyMessagingContainerResources(parameters, "connect");
                if (compactResources) { parameters.HostConfig!.Memory = 1L << 30; parameters.HostConfig.MemorySwap = parameters.HostConfig.Memory; }
            })
            .WithLabel("fullnet.cdc.resource-profile", compactResources ? "functional-compact" : "standard")
            // 镜像默认 Xmx2G 与容器 2 GiB 上限冲突；为元空间、线程栈和插件扫描保留真实余量。
            .WithEnvironment("HEAP_OPTS", compactResources ? "-Xms256M -Xmx512M" : "-Xms256M -Xmx768M")
            .WithNetwork(network)
            .WithExtraHost("host.docker.internal", "host-gateway")
            .WithPortBinding(8083, true)
            .WithBindMount(
                javaCryptoPolicy,
                "/etc/crypto-policies/back-ends/java.config")
            .WithEnvironment("BOOTSTRAP_SERVERS", connectProxyPort is null ? $"{KafkaAlias}:{KafkaInternalPort}" : $"host.docker.internal:{connectProxyPort}")
            .WithEnvironment("GROUP_ID", "fullnet-debezium-connect-test")
            .WithEnvironment("CONFIG_STORAGE_TOPIC", "fullnet.dev.shadow.internal.connect-config")
            .WithEnvironment("OFFSET_STORAGE_TOPIC", "fullnet.dev.shadow.internal.connect-offsets")
            .WithEnvironment("STATUS_STORAGE_TOPIC", "fullnet.dev.shadow.internal.connect-status")
            .WithEnvironment("CONFIG_STORAGE_REPLICATION_FACTOR", "1")
            .WithEnvironment("OFFSET_STORAGE_REPLICATION_FACTOR", "1")
            .WithEnvironment("STATUS_STORAGE_REPLICATION_FACTOR", "1")
            .WithEnvironment("KEY_CONVERTER", "org.apache.kafka.connect.json.JsonConverter")
            .WithEnvironment("VALUE_CONVERTER", "org.apache.kafka.connect.json.JsonConverter")
            .WithEnvironment("KEY_CONVERTER_SCHEMAS_ENABLE", "false")
            .WithEnvironment("VALUE_CONVERTER_SCHEMAS_ENABLE", "false")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Kafka Connect started"));
        // 紧凑功能档仍提交真实Offset，缩短无数据快照就绪时的串行等待，不替代来源证明。
        if (compactResources) connectBuilder = connectBuilder.WithEnvironment("OFFSET_FLUSH_INTERVAL_MS", "1000");
        if (!string.IsNullOrEmpty(filterJar))
            connectBuilder = connectBuilder.WithBindMount(filterJar, "/kafka/connect/fullnet-stream-filter/fullnet-outbox-stream-filter.jar", AccessMode.ReadOnly);
        var connect = connectBuilder.Build();

        try
        {
            await kafka.StartAsync().ConfigureAwait(false);
            onKafkaStarted?.Invoke(containerHostPort);
            await connect.StartAsync().ConfigureAwait(false);
        }
        catch
        {
            await connect.DisposeAsync().ConfigureAwait(false);
            await kafka.DisposeAsync().ConfigureAwait(false);
            await network.DeleteAsync().ConfigureAwait(false);
            throw;
        }

        return new CdcDebeziumPipelineEnvironment(
            network,
            kafka,
            connect,
            bootstrapServers: $"127.0.0.1:{observerPort}",
            connectBaseUri: new Uri($"http://127.0.0.1:{connect.GetMappedPublicPort(8083)}/"),
            hostGateway: "host.docker.internal",
            internalKafkaBootstrapServers: $"{KafkaAlias}:{KafkaInternalPort}")
        {
            KafkaMappedPort = containerHostPort,
            ContainerClientBootstrapServers = $"host.docker.internal:{advertisedHostPort ?? containerHostPort}",
        };
    }

    public KafkaConnectAdminClient CreateConnectAdminClient() =>
        new(ConnectBaseUri);

    public async Task InterruptBrokerAsync(TimeSpan? duration = null, CancellationToken cancellationToken = default,
        Func<CancellationToken, Task>? observeWhilePaused = null)
    {
        var pause = duration ?? TimeSpan.FromSeconds(1);
        if (pause <= TimeSpan.Zero || pause > TimeSpan.FromSeconds(60)) throw new ArgumentOutOfRangeException(nameof(duration));
        await _kafka.PauseAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (observeWhilePaused is null) await Task.Delay(pause, cancellationToken).ConfigureAwait(false);
            else
            {
                await Task.Delay(pause / 2, cancellationToken).ConfigureAwait(false);
                await observeWhilePaused(cancellationToken).ConfigureAwait(false);
                await Task.Delay(pause / 2, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { await _kafka.UnpauseAsync(CancellationToken.None).ConfigureAwait(false); }
    }

    public async ValueTask DisposeAsync()
    {
        await _connect.DisposeAsync().ConfigureAwait(false);
        await _kafka.DisposeAsync().ConfigureAwait(false);
        await _network.DeleteAsync().ConfigureAwait(false);
    }

    private static ushort AllocateAvailableHostPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string ResolveJavaCryptoPolicyPath()
    {
        var repositoryRoot = FullNetApiFactory.FindRepositoryRoot();
        return Path.Combine(
            repositoryRoot,
            "tests",
            "Full.NET.IntegrationTests",
            "Messaging",
            "Assets",
            "debezium-connect-java.config");
    }
}

/// <summary>
/// 按需启动并复用 CDC 测试栈；生命周期由 <see cref="SharedDatabaseFixture"/> 统一清理。
/// </summary>
public static class CdcDebeziumPipelineFixture
{
    private static CdcDebeziumPipelineEnvironment? _environment;
    private static readonly SemaphoreSlim StartLock = new(1, 1);

    public static async Task<CdcDebeziumPipelineEnvironment> GetOrStartAsync()
    {
        if (_environment is not null)
        {
            return _environment;
        }

        await StartLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_environment is not null)
            {
                return _environment;
            }

            _environment = await CdcDebeziumPipelineEnvironment.StartAsync().ConfigureAwait(false);
            return _environment;
        }
        finally
        {
            StartLock.Release();
        }
    }

    internal static async Task DisposeAsync()
    {
        if (_environment is null)
        {
            return;
        }

        await StartLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_environment is not null)
            {
                await _environment.DisposeAsync().ConfigureAwait(false);
                _environment = null;
            }
        }
        finally
        {
            StartLock.Release();
        }
    }
}
