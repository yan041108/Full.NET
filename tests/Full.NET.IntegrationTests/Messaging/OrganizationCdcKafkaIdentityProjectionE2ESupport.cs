using Confluent.Kafka;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modularity.Messaging;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.OrganizationUnitProjection;
using Full.NET.Modules.Identity;
using Full.NET.Serialization.MemoryPack;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// Organization 真实 CDC 试点：Inbox + Identity 投影消费辅助。
/// </summary>
internal static class OrganizationCdcKafkaIdentityProjectionE2ESupport
{
    internal const string PilotUnitName = "pilot-unit";

    internal static async Task SeedPilotStreamOwnershipAsync(DatabaseOptions options) =>
        await OrganizationUnitCdcKafkaEndToEndSupport.SeedCdcKafkaStreamOwnershipAsync(options);

    internal static async Task<InboxConsumeStatus> ConsumeOrganizationEventThroughInboxAsync(
        DatabaseOptions options,
        ConsumeResult<string, byte[]> consumed)
    {
        var reader = new KafkaEnvelopeReader();
        if (!reader.TryRead(consumed, out var envelope, out var failureCode)
            || envelope is null)
        {
            throw new InvalidOperationException($"Kafka envelope invalid: {failureCode}");
        }

        await using var services = BuildIdentityProjectionInboxServices(options);
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var subscription = scope.ServiceProvider
            .GetRequiredService<IIntegrationEventSubscription>();
        var catalog = new IntegrationEventSubscriptionCatalog(
            [IdentityIntegrationEventTopicDefinitions.OrganizationUnitChanged],
            [subscription]);
        var dispatcher = new IntegrationEventConsumerDispatcher(
            scope.ServiceProvider.GetRequiredService<ICommandTransaction>(),
            scope.ServiceProvider.GetRequiredService<IIntegrationEventInbox>(),
            catalog,
            new PermissiveOwnershipGate(),
            new CdcOwnerResolver(),
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>());
        var result = await dispatcher.ConsumeAsync(
            subscription.ConsumerName,
            envelope,
            subscription,
            CancellationToken.None).ConfigureAwait(false);
        return result.Status;
    }

    internal static async Task<(Guid TenantId, Guid UnitId)> ReadLatestPilotOutboxIdentityAsync(
        DatabaseOptions options)
    {
        await using var connection = new MySqlConnection(options.ConnectionString);
        var row = await connection.QuerySingleAsync<(Guid Id, byte[] Payload)>(
            """
            SELECT Id, Payload
            FROM fn_messaging_outbox_event
            WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
            ORDER BY OccurredAtUtc DESC
            LIMIT 1
            """,
            new
            {
                MessageType = EventDeliveryPilotTestSupport.PilotEventType,
                SchemaVersion = EventDeliveryPilotTestSupport.PilotSchemaVersion,
            });
        await using var scope = BuildIdentityProjectionInboxServices(options).CreateAsyncScope();
        var serializer = scope.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>();
        var payload = serializer.Deserialize<IdentityOrganizationUnitChangedIntegrationEvent>(
            row.Payload);
        return (payload.TenantId, payload.UnitId);
    }

    internal static async Task<bool> ProjectionExistsAsync(
        DatabaseOptions options,
        Guid tenantId,
        Guid unitId,
        string expectedName) =>
        await OrganizationUnitCdcKafkaEndToEndSupport.ProjectionExistsAsync(
            options,
            tenantId,
            unitId,
            expectedName);

    private static ServiceProvider BuildIdentityProjectionInboxServices(DatabaseOptions options)
    {
        var configuration = MessagingOutboxTestSupport.CreateConfiguration(options);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetDapper(configuration, "Testing");
        services.AddFullNetMemoryPack();
        services.AddScoped<OrganizationUnitProjectionWriter>();
        services.AddScoped<IIntegrationEventHandler, OrganizationUnitChangedIntegrationEventHandler>();
        services.AddScoped<IIntegrationEventSubscription, OrganizationUnitChangedKafkaSubscription>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private sealed class PermissiveOwnershipGate : IEventStreamOwnershipGate
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

    private sealed class CdcOwnerResolver : IEffectiveEventDeliveryOwnerResolver
    {
        public Task<EventDeliveryOwner> GetDeliveryOwnerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(EventDeliveryOwner.CdcKafka);
    }
}
