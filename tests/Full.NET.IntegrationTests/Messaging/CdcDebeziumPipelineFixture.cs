using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
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

    public static async Task<CdcDebeziumPipelineEnvironment> StartAsync()
    {
        var network = new NetworkBuilder().Build();
        await network.CreateAsync().ConfigureAwait(false);
        var kafkaExternalHostPort = AllocateAvailableHostPort();

        var kafka = new ContainerBuilder(KafkaImage)
            .WithNetwork(network)
            .WithNetworkAliases(KafkaAlias)
            .WithPortBinding(kafkaExternalHostPort, KafkaExternalPort)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment(
                "KAFKA_LISTENERS",
                $"PLAINTEXT://0.0.0.0:{KafkaInternalPort},EXTERNAL://0.0.0.0:{KafkaExternalPort},CONTROLLER://0.0.0.0:9093")
            .WithEnvironment(
                "KAFKA_ADVERTISED_LISTENERS",
                $"PLAINTEXT://{KafkaAlias}:{KafkaInternalPort},EXTERNAL://127.0.0.1:{kafkaExternalHostPort}")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment(
                "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP",
                "CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT,EXTERNAL:PLAINTEXT")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", $"1@{KafkaAlias}:9093")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
            .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "true")
            .WithEnvironment("CLUSTER_ID", "fullnet-cdc-e2e-kafka-cluster")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Kafka Server started"))
            .Build();

        var javaCryptoPolicy = ResolveJavaCryptoPolicyPath();
        var connect = new ContainerBuilder(DebeziumImage)
            .WithNetwork(network)
            .WithExtraHost("host.docker.internal", "host-gateway")
            .WithPortBinding(8083, true)
            .WithBindMount(
                javaCryptoPolicy,
                "/etc/crypto-policies/back-ends/java.config")
            .WithEnvironment("BOOTSTRAP_SERVERS", $"{KafkaAlias}:{KafkaInternalPort}")
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
                .UntilMessageIsLogged("Kafka Connect started"))
            .Build();

        try
        {
            await kafka.StartAsync().ConfigureAwait(false);
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
            bootstrapServers: $"127.0.0.1:{kafkaExternalHostPort}",
            connectBaseUri: new Uri($"http://127.0.0.1:{connect.GetMappedPublicPort(8083)}/"),
            hostGateway: "host.docker.internal",
            internalKafkaBootstrapServers: $"{KafkaAlias}:{KafkaInternalPort}");
    }

    public KafkaConnectAdminClient CreateConnectAdminClient() =>
        new(ConnectBaseUri);

    public async Task InterruptBrokerAsync()
    {
        await _kafka.PauseAsync().ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        await _kafka.UnpauseAsync().ConfigureAwait(false);
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
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
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
