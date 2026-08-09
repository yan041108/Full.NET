extern alias workerhost;

using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;
using Full.NET.Modules.Messaging.Features.RollbackDeliveryOwner;
using Full.NET.Modules.Messaging.Persistence;
using Full.NET.Serialization.MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using WorkerHost = workerhost::Full.NET.Host.Worker;

namespace Full.NET.IntegrationTests.Messaging;

internal static class EventDeliveryPilotTestSupport
{
    internal const int PilotSchemaVersion = 1;

    internal static string PilotEventType =>
        IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged;

    internal static async Task<ServiceProvider> BuildPilotServicesAsync(DatabaseOptions options)
    {
        await MessagingOutboxTestSupport.MigrateAsync(options);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = options.Provider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] = options.ConnectionString,
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                    options.MySqlGuidStorageMode.ToString(),
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetDapper(configuration, "Testing");
        services.AddFullNetMessagePack();
        var pilotTopic = MessagingTopicDefinitions.OrganizationUnitChanged;
        services.AddSingleton<IIntegrationEventHandler, PilotEventRecordingHandler>();
        services.TryAddScoped<EventStreamOwnershipStore>();
        services.TryAddScoped<IEventStreamOwnershipStore>(
            provider => provider.GetRequiredService<EventStreamOwnershipStore>());
        services.TryAddScoped<IEffectiveEventDeliveryOwnerResolver, EffectiveEventDeliveryOwnerResolver>();
        services.TryAddScoped<DeliveryCutoverService>();
        services.TryAddScoped<DeliveryRollbackService>();
        services.TryAddScoped<
            ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>,
            MessagingDomainAuditWriter>();
        services.AddSingleton(provider =>
            new IntegrationEventSubscriptionCatalog(
                [pilotTopic],
                provider.GetServices<IIntegrationEventSubscription>()));
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    internal static AsyncServiceScope CreateHostScope(IServiceProvider provider)
    {
        var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        return scope;
    }

    internal static async Task WritePilotOutboxEventAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenantAccessor.SetHost();
        var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var commandExecutor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var databaseOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<DatabaseOptions>>()
            .Value;
        var idGenerator = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var tenantId = idGenerator.NewId();
        var unitId = idGenerator.NewId();
        var partitionKey = $"tenant:{tenantId:D}";
        var correlationId = $"pilot-{unitId:N}";
        await writer.AddAsync(
            PilotEventType,
            PilotSchemaVersion,
            new IdentityOrganizationUnitChangedIntegrationEvent(
                tenantId,
                unitId,
                "pilot-unit",
                true,
                1,
                clock.UtcNow),
            cancellationToken);
        await MirrorLatestLegacyOutboxToAppendOnlyAsync(
            commandExecutor,
            databaseOptions.Provider,
            partitionKey,
            correlationId,
            cancellationToken);
    }

  private static Task MirrorLatestLegacyOutboxToAppendOnlyAsync(
        ICommandExecutor commandExecutor,
        DatabaseProvider provider,
        string partitionKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var parameters = new
        {
            MessageType = PilotEventType,
            SchemaVersion = PilotSchemaVersion,
            PartitionKey = partitionKey,
            CorrelationId = correlationId,
            Producer = "fullnet.messaging.pilot.tests",
        };
        var statement = provider switch
        {
            DatabaseProvider.SqlServer => PilotOutboxMirrorSql.SqlServer,
            DatabaseProvider.MySql => PilotOutboxMirrorSql.MySql,
            _ => throw new NotSupportedException(
                $"Database provider '{provider}' is not supported."),
        };
        return commandExecutor.ExecuteAsync(statement, parameters, cancellationToken);
    }

      private static class PilotOutboxMirrorSql
    {
        internal static readonly SqlStatement SqlServer = new(
            "messaging.pilot.mirror_legacy_outbox.sqlserver",
            """
            INSERT INTO fn_messaging_outbox_event
                (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
                 CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
            SELECT TOP 1
                message.Id,
                message.MessageType,
                message.SchemaVersion,
                message.ContentType,
                message.TenantId,
                @PartitionKey,
                @CorrelationId,
                NULL,
                NULL,
                @Producer,
                message.Payload,
                message.OccurredAtUtc
            FROM fn_outbox_message AS message
            WHERE message.MessageType = @MessageType
              AND message.SchemaVersion = @SchemaVersion
            ORDER BY message.OccurredAtUtc DESC, message.Id DESC
            """,
            SqlDataScope.Global);

        internal static readonly SqlStatement MySql = new(
            "messaging.pilot.mirror_legacy_outbox.mysql",
            """
            INSERT INTO fn_messaging_outbox_event
                (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
                 CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
            SELECT
                message.Id,
                message.MessageType,
                message.SchemaVersion,
                message.ContentType,
                message.TenantId,
                @PartitionKey,
                @CorrelationId,
                NULL,
                NULL,
                @Producer,
                message.Payload,
                message.OccurredAtUtc
            FROM fn_outbox_message AS message
            WHERE message.MessageType = @MessageType
              AND message.SchemaVersion = @SchemaVersion
            ORDER BY message.OccurredAtUtc DESC, message.Id DESC
            LIMIT 1
            """,
            SqlDataScope.Global);
    }

    internal static async Task ProcessOutboxOnceAsync(
        ServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var processor = new WorkerHost.OutboxProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IClock>(),
            Options.Create(new WorkerHost.OutboxWorkerOptions
            {
                BatchSize = 10,
                MaxConcurrency = 1,
            }),
            NullLogger<WorkerHost.OutboxProcessor>.Instance);
        await processor.ProcessOnceAsync(cancellationToken);
    }

    internal sealed class PilotEventRecordingHandler : IIntegrationEventHandler
    {
        private int _handledCount;

        public int HandledCount => _handledCount;

        public string EventType => PilotEventType;

        public int SchemaVersion => PilotSchemaVersion;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            HandleAsync(payload, cancellationToken);

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _handledCount);
            return Task.CompletedTask;
        }
    }
}
