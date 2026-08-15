using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.Dapper.Outbox;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modularity.Messaging;
using Full.NET.Serialization.MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 为容量 Worker/Outbox 场景组装生产 Inbox、Outbox 与 Kafka 处理依赖。
/// </summary>
public static class KafkaCapacityServiceFactory
{
    public static ServiceProvider BuildWorkerServices(
        KafkaCapacityConfiguration configuration,
        KafkaCapacityWorkerObserver observer)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(observer);
        var services = CreateBaseServices(configuration);
        services.AddSingleton(observer);
        services.AddScoped<IIntegrationEventSubscription, KafkaCapacityWorkerSubscription>();
        RegisterWorkerMessaging(services);
        RegisterKafkaProcessor(services);
        return BuildProvider(services);
    }

    public static ServiceProvider BuildOutboxCdcServices(
        KafkaCapacityConfiguration configuration,
        KafkaCapacityWorkerObserver observer)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(observer);
        var services = CreateBaseServices(configuration);
        services.AddSingleton(observer);
        services.AddScoped<IIntegrationEventSubscription, KafkaCapacityWorkerSubscription>();
        RegisterWorkerMessaging(services);
        RegisterKafkaProcessor(services);
        services.RemoveAll<IIntegrationEventSerializer>();
        services.AddSingleton<IIntegrationEventSerializer, KafkaCapacityRawIntegrationEventSerializer>();
        services.RemoveAll<IEffectiveEventDeliveryOwnerResolver>();
        services.AddSingleton<IEffectiveEventDeliveryOwnerResolver, KafkaCapacityCdcOwnerResolver>();
        services.RemoveAll<IEventStreamOwnershipGate>();
        services.AddScoped<IEventStreamOwnershipGate, KafkaCapacityPermissiveOwnershipGate>();
        return BuildProvider(services);
    }

    private static ServiceCollection CreateBaseServices(KafkaCapacityConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<KafkaMessagingOptions>()
            .Configure(options => CopyKafkaOptions(configuration.Kafka, options));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetMessagePack();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());

        var values = new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Provider"] =
                configuration.Database.Provider.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] =
                configuration.Database.ConnectionString,
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] =
                configuration.Database.CommandTimeoutSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                configuration.Database.MySqlGuidStorageMode.ToString(),
            [$"{MessagingOutboxOptions.SectionName}:Mode"] =
                MessagingOutboxMode.AppendOnlyV2.ToString(),
        };
        var databaseConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        services.AddFullNetDapper(databaseConfiguration, "Capacity");
        services.AddFullNetModularity();
        return services;
    }

    private static void RegisterWorkerMessaging(IServiceCollection services)
    {
        services.AddScoped(_ => IntegrationEventTopicDefinition.Create(
            KafkaCapacityWorkerContracts.TopicCode,
            KafkaCapacityWorkerContracts.EventType,
            KafkaCapacityWorkerContracts.SchemaVersion,
            EventDeliveryOwner.CdcKafka));
        services.RemoveAll<IIntegrationEventSubscriptionCatalog>();
        services.RemoveAll<IntegrationEventSubscriptionCatalog>();
        services.AddScoped<IIntegrationEventSubscriptionCatalog>(provider =>
            new IntegrationEventSubscriptionCatalog(
                provider.GetServices<IntegrationEventTopicDefinition>(),
                provider.GetServices<IIntegrationEventSubscription>()));
        services.AddScoped(provider =>
            (IntegrationEventSubscriptionCatalog)provider
                .GetRequiredService<IIntegrationEventSubscriptionCatalog>());
    }

    private static void RegisterKafkaProcessor(IServiceCollection services)
    {
        services.AddSingleton<KafkaEnvelopeReader>();
        services.AddSingleton<KafkaOffsetCommitter>();
        services.AddSingleton<KafkaFailureClassifier>();
        services.AddSingleton<KafkaMessagingProducer>();
        services.AddSingleton<KafkaRetryRouter>();
        services.AddSingleton<KafkaDeadLetterPublisher>();
        services.AddSingleton<KafkaConsumerMessageProcessor>();
    }

    private static ServiceProvider BuildProvider(ServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

    private static void CopyKafkaOptions(
        KafkaMessagingOptions source,
        KafkaMessagingOptions destination)
    {
        foreach (var property in typeof(KafkaMessagingOptions).GetProperties()
                     .Where(static property => property.CanRead && property.CanWrite))
        {
            property.SetValue(destination, property.GetValue(source));
        }
    }

    private sealed class KafkaCapacityCdcOwnerResolver : IEffectiveEventDeliveryOwnerResolver
    {
        public Task<EventDeliveryOwner> GetDeliveryOwnerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(EventDeliveryOwner.CdcKafka);
    }

    private sealed class KafkaCapacityPermissiveOwnershipGate : IEventStreamOwnershipGate
    {
        public Task<bool> AcquireProducerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> AcquireConsumerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> AcquireOwnershipChangeAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}

/// <summary>
/// 允许容量 Runner 在写入 Outbox 前绑定确定性 EventId。
/// </summary>
public sealed class KafkaCapacitySequenceIdGenerator : IIdGenerator
{
    private static readonly AsyncLocal<Guid> NextId = new();

    public void SetNext(Guid eventId) => NextId.Value = eventId;

    public Guid NewId()
    {
        var eventId = NextId.Value;
        if (eventId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Kafka capacity id generator requires SetNext before NewId.");
        }

        NextId.Value = Guid.Empty;
        return eventId;
    }
}

