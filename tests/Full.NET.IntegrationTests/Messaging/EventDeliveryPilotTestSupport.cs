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
using Full.NET.Modules.Identity;
using Full.NET.Modules.Messaging;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;
using Full.NET.Modules.Messaging.Features.RollbackDeliveryOwner;
using Full.NET.Modules.Messaging.Persistence;
using Full.NET.Serialization.MemoryPack;
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

    internal static async Task<ServiceProvider> BuildPilotServicesAsync(
        DatabaseOptions options,
        bool cutoverEnabled = true,
        IEventDeliveryRollbackReadinessReader? rollbackReadinessReader = null)
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
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetDapper(configuration, "Testing");
        services.AddFullNetMemoryPack();
        services.AddOptions<DeliveryCutoverOptions>()
            .Configure(configured => configured.Enabled = cutoverEnabled);
        services.AddSingleton(
            rollbackReadinessReader
            ?? new FailClosedEventDeliveryRollbackReadinessReader());
        var pilotTopic = IdentityIntegrationEventTopicDefinitions.OrganizationUnitChanged;
        services.AddSingleton<IIntegrationEventHandler, PilotEventRecordingHandler>();
        services.TryAddScoped<EventStreamOwnershipStore>();
        services.TryAddScoped<IEventStreamOwnershipStore>(
            provider => provider.GetRequiredService<EventStreamOwnershipStore>());
        services.RemoveAll<IEffectiveEventDeliveryOwnerResolver>();
        services.AddScoped<IEffectiveEventDeliveryOwnerResolver, EffectiveEventDeliveryOwnerResolver>();
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
        var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        var idGenerator = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var tenantId = idGenerator.NewId();
        var unitId = idGenerator.NewId();
        var partitionKey = $"tenant:{tenantId:D}";
        var correlationId = $"pilot-{unitId:N}";
        await transaction.ExecuteAsync(
            async token =>
            {
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
                    IntegrationEventMetadata.Create(
                        partitionKey,
                        "fullnet.messaging.pilot.tests",
                        correlationId),
                    token);
                return 0;
            },
            cancellationToken);
    }

    internal static async Task WritePilotOutboxEventHoldingTransactionAsync(
        IServiceProvider provider,
        TaskCompletionSource producerInserted,
        Task releaseProducer,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        var idGenerator = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var tenantId = idGenerator.NewId();
        var unitId = idGenerator.NewId();

        await transaction.ExecuteAsync(
            async token =>
            {
                await writer.AddAsync(
                    PilotEventType,
                    PilotSchemaVersion,
                    new IdentityOrganizationUnitChangedIntegrationEvent(
                        tenantId,
                        unitId,
                        "pilot-concurrent-unit",
                        true,
                        1,
                        clock.UtcNow),
                    IntegrationEventMetadata.Create(
                        $"tenant:{tenantId:D}",
                        "fullnet.messaging.pilot.tests"),
                    token);
                producerInserted.TrySetResult();
                await releaseProducer.WaitAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 向 fn_outbox_message 直接写入任意 MessageType 的 Legacy Outbox 原始 pending 消息，
    /// 用于构造"其他 Legacy 流有积压"的场景。payload 使用空字节数组。
    /// </summary>
    internal static async Task WriteRawLegacyOutboxEventAsync(
        IServiceProvider provider,
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenantAccessor.SetHost();
        var commandExecutor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var databaseOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<DatabaseOptions>>()
            .Value;
        var idGenerator = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var id = idGenerator.NewId();
        var tenantId = idGenerator.NewId();
        var parameters = new
        {
            Id = id,
            MessageType = eventType,
            SchemaVersion = schemaVersion,
            ContentType = "application/x-memorypack",
            Payload = Array.Empty<byte>(),
            TenantId = tenantId,
            TraceId = (string?)null,
            OccurredAtUtc = clock.UtcNow,
        };
        var statement = databaseOptions.Provider switch
        {
            DatabaseProvider.SqlServer => RawLegacyOutboxInsertSql.SqlServer,
            DatabaseProvider.MySql => RawLegacyOutboxInsertSql.MySql,
            _ => throw new NotSupportedException(
                $"Database provider '{databaseOptions.Provider}' is not supported."),
        };
        var affected = await commandExecutor
            .ExecuteAsync(statement, parameters, cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Insert raw legacy outbox event affected {affected} rows, expected 1.");
        }
    }

      private static class RawLegacyOutboxInsertSql
    {
        internal static readonly SqlStatement SqlServer = new(
            "messaging.pilot.insert_raw_legacy_outbox.sqlserver",
            """
            INSERT INTO fn_outbox_message
                (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId,
                 Payload, OccurredAtUtc, Attempts)
            VALUES
                (@Id, @MessageType, @SchemaVersion, @ContentType, @TenantId, @TraceId,
                 @Payload, @OccurredAtUtc, 0)
            """,
            SqlDataScope.Global);

        internal static readonly SqlStatement MySql = new(
            "messaging.pilot.insert_raw_legacy_outbox.mysql",
            """
            INSERT INTO fn_outbox_message
                (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId,
                 Payload, OccurredAtUtc, Attempts)
            VALUES
                (@Id, @MessageType, @SchemaVersion, @ContentType, @TenantId, @TraceId,
                 @Payload, @OccurredAtUtc, 0)
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
