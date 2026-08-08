using Confluent.Kafka;
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
        return new KafkaTestEnvironment(container);
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

    public IConsumer<string, byte[]> CreateConsumer(string groupId, string clientId)
    {
        var options = CreateOptions(clientId);
        return new ConsumerBuilder<string, byte[]>(options.BuildConsumerConfig(groupId)).Build();
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

    public async Task RestartAsync()
    {
        await _container.StopAsync().ConfigureAwait(false);
        await _container.StartAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync().ConfigureAwait(false);
    }
}

[TestClass]
public sealed class KafkaFixture
{
    private static KafkaTestEnvironment? _environment;

    public static KafkaTestEnvironment Environment =>
        _environment ?? throw new InvalidOperationException("Kafka fixture is not initialized.");

    [AssemblyInitialize]
    public static async Task AssemblyInitialize(TestContext context)
    {
        _environment = await KafkaTestEnvironment.StartAsync().ConfigureAwait(false);
    }

    [AssemblyCleanup]
    public static async Task AssemblyCleanup()
    {
        if (_environment is not null)
        {
            await _environment.DisposeAsync().ConfigureAwait(false);
            _environment = null;
        }
    }
}



