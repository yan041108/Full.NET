using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Kafka;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// 共享 Kafka Testcontainer；使用 apache/kafka:4.1.2。
/// </summary>
public sealed class KafkaTestEnvironment : IAsyncDisposable
{
    private readonly KafkaContainer _container;

    private KafkaTestEnvironment(KafkaContainer container)
    {
        _container = container;
        BootstrapServers = container.GetBootstrapAddress();
    }

    public string BootstrapServers { get; }

    public static async Task<KafkaTestEnvironment> StartAsync()
    {
        var container = new KafkaBuilder("apache/kafka:4.1.2")
            .Build();
        await container.StartAsync().ConfigureAwait(false);
        var environment = new KafkaTestEnvironment(container);
        await environment.WaitForIdempotentProducerAsync().ConfigureAwait(false);
        return environment;
    }

    private async Task WaitForIdempotentProducerAsync()
    {
        const string readinessTopic = "fullnet.integration.readiness.v1";
        await EnsureTopicsAsync(readinessTopic).ConfigureAwait(false);
        using var producer = CreateProducer("fullnet-integration-readiness");
        var delivery = await producer.ProduceAsync(
                readinessTopic,
                new Message<string, byte[]>
                {
                    Key = "readiness",
                    Value = [1],
                })
            .ConfigureAwait(false);
        if (delivery.Status != PersistenceStatus.Persisted)
        {
            throw new InvalidOperationException(
                $"Kafka readiness probe was not persisted: {delivery.Status}.");
        }
    }

    public KafkaMessagingOptions CreateOptions(string clientId) =>
        new()
        {
            Enabled = true,
            BootstrapServers = BootstrapServers,
            ClientId = clientId,
            ConsumerInstanceId = $"{clientId}-01",
            SecurityProtocol = "Plaintext",
            MessageMaxBytes = 1_048_576,
            RetryStages = ["5s", "1m", "15m"],
            DeliveryTimeoutMilliseconds = 30_000,
        };

    public IProducer<string, byte[]> CreateProducer(string clientId)
    {
        var options = CreateOptions(clientId);
        return new ProducerBuilder<string, byte[]>(options.BuildProducerConfig()).Build();
    }

    public IConsumer<string, byte[]> CreateConsumer(
        string groupId,
        string clientId,
        string? consumerInstanceId = null)
    {
        var options = CreateOptions(clientId);
        if (!string.IsNullOrWhiteSpace(consumerInstanceId))
        {
            options.ConsumerInstanceId = consumerInstanceId;
        }

        return new ConsumerBuilder<string, byte[]>(options.BuildConsumerConfig(groupId)).Build();
    }

    public async Task EnsureTopicsAsync(params string[] topics)
    {
        await EnsureTopicsAsync(
            partitions: 1,
            replicationFactor: 1,
            topics).ConfigureAwait(false);
    }

    public async Task EnsureTopicsAsync(
        int partitions,
        short replicationFactor,
        params string[] topics)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = BootstrapServers,
        }).Build();
        try
        {
            await admin.CreateTopicsAsync(
                topics.Distinct(StringComparer.Ordinal).Select(topic => new TopicSpecification
                {
                    Name = topic,
                    NumPartitions = partitions,
                    ReplicationFactor = replicationFactor,
                })).ConfigureAwait(false);
        }
        catch (CreateTopicsException exception) when (
            exception.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            // 共享 fixture 重跑同名 Topic 时保持幂等。
        }
    }

    internal KafkaRetryRouter CreateRetryRouter(string clientId)
    {
        var options = Options.Create(CreateOptions(clientId));
        return new KafkaRetryRouter(
            options,
            new KafkaMessagingProducer(options),
            NullLogger<KafkaRetryRouter>.Instance);
    }

    internal KafkaDeadLetterPublisher CreateDeadLetterPublisher(string clientId)
    {
        var options = Options.Create(CreateOptions(clientId));
        return new KafkaDeadLetterPublisher(
            options,
            new KafkaMessagingProducer(options),
            NullLogger<KafkaDeadLetterPublisher>.Instance);
    }

    public async Task InterruptBrokerAsync()
    {
        // Stop/Start 会让 Testcontainers 重新分配宿主端口，旧 Consumer 无法验证同一 Broker 地址的恢复。
        // Pause/Unpause 保持监听端点不变，同时制造真实的网络不可用窗口。
        await _container.PauseAsync().ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        await _container.UnpauseAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// 按需启动并复用 Kafka 容器；生命周期由 <see cref="SharedDatabaseFixture"/> 统一清理。
/// </summary>
public static class KafkaFixture
{
    private static KafkaTestEnvironment? _environment;
    private static readonly SemaphoreSlim StartLock = new(1, 1);

    public static async Task<KafkaTestEnvironment> GetOrStartAsync()
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

            _environment = await KafkaTestEnvironment.StartAsync().ConfigureAwait(false);
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
