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
using Full.NET.Modules.Messaging.Features.ManageShadowCapture;
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
        IEventDeliveryRollbackReadinessReader? rollbackReadinessReader = null,
        bool seedCutoverEvidence = true, Action<IServiceCollection>? configureServices = null)
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
        services.AddSingleton<Microsoft.AspNetCore.Http.IHttpContextAccessor>(new TestOperatorContextAccessor
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim("sub", Guid.CreateVersion7().ToString()),
                     new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "运维协议测试")], "test")),
            },
        });
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetDapper(configuration, "Testing");
        services.AddFullNetMemoryPack();
        services.Configure<ShadowCaptureOptions>(capture => { capture.Enabled = true; capture.EventType = PilotEventType; });
        services.AddScoped<ShadowCaptureStore>();
        services.AddScoped<ShadowCaptureService>();
        services.AddOptions<DeliveryCutoverOptions>()
            .Configure(configured => configured.Enabled = cutoverEnabled);
        if (rollbackReadinessReader is null)
            services.AddSingleton<IEventDeliveryRollbackReadinessReader, FailClosedEventDeliveryRollbackReadinessReader>();
        else
            services.AddScoped<IEventDeliveryRollbackReadinessReader>(provider => new JournaledTestReadinessReader(
                rollbackReadinessReader, provider.GetRequiredService<IEventDeliveryRollbackJournal>()));
        services.AddSingleton<IEventDeliveryCutoverProbe, PilotCutoverProbe>();
        services.AddScoped<DeliveryCutoverEvidenceService>();
        var pilotTopic = IdentityIntegrationEventTopicDefinitions.OrganizationUnitChanged;
        services.AddSingleton(pilotTopic);
        services.AddSingleton<IIntegrationEventHandler, PilotEventRecordingHandler>();
        services.TryAddScoped<EventStreamOwnershipStore>();
        services.TryAddScoped<IEventStreamOwnershipStore>(
            provider => provider.GetRequiredService<EventStreamOwnershipStore>());
        services.RemoveAll<IEffectiveEventDeliveryOwnerResolver>();
        services.AddScoped<IEffectiveEventDeliveryOwnerResolver>(provider =>
            new EffectiveEventDeliveryOwnerResolver(
                () => provider.GetRequiredService<IntegrationEventSubscriptionCatalog>(),
                provider.GetRequiredService<IEventStreamOwnershipStore>()));
        services.TryAddScoped<DeliveryCutoverService>();
        services.TryAddScoped<DeliveryRollbackService>();
        services.TryAddScoped<DeliveryRollbackJournal>();
        services.TryAddScoped<IEventDeliveryRollbackJournal>(provider => provider.GetRequiredService<DeliveryRollbackJournal>());
        services.TryAddScoped<DeliveryRollbackRecoveryService>();
        services.TryAddScoped<
            ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>,
            MessagingDomainAuditWriter>();
        services.AddSingleton(provider =>
            new IntegrationEventSubscriptionCatalog(
                [pilotTopic],
                provider.GetServices<IIntegrationEventSubscription>()));
        services.AddSingleton<IIntegrationEventSubscriptionCatalog>(provider => provider.GetRequiredService<IntegrationEventSubscriptionCatalog>());
        configureServices?.Invoke(services);
        var result = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        if (seedCutoverEvidence && cutoverEnabled) await SeedCutoverEvidenceAsync(result);
        return result;
    }

    /// <summary>测试明确建立权威样本及持久证明，生产门禁始终保持启用。</summary>
    internal static async Task SeedCutoverEvidenceAsync(IServiceProvider provider)
    {
        await using var scope = CreateHostScope(provider);
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var tenantId = ids.NewId();
        var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        var ownership = scope.ServiceProvider.GetRequiredService<IEventStreamOwnershipStore>();
        // 夹具显式预置持久 Legacy 所有权；生产控制 API 不得通过隐式补行绕过生产者屏障。
        await transaction.ExecuteAsync(async token =>
        {
            if (await ownership.FindAsync(PilotEventType, PilotSchemaVersion, token) is null)
                await ownership.UpsertAsync(new(PilotEventType, PilotSchemaVersion,
                    IdentityIntegrationEventTopicDefinitions.OrganizationUnitChanged.TopicCode,
                    EventDeliveryOwner.LegacyPolling, EventDeliveryOwner.LegacyPolling, ids.NewId(), now,
                    null, null, "fixture Legacy registration", null, null, now, now), token);
            return 0;
        }, CancellationToken.None);
        var capture = scope.ServiceProvider.GetRequiredService<ShadowCaptureService>();
        var previous = await capture.FindAsync(PilotEventType, PilotSchemaVersion, CancellationToken.None);
        var started = await capture.StartAsync(new(ids.NewId(), previous.IsSuccess ? previous.Value!.Generation : null,
            PilotEventType, PilotSchemaVersion, 1, 60, "fixture real Legacy capture"), CancellationToken.None);
        Assert.IsTrue(started.IsSuccess, started.Error?.Message);
        var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        await transaction.ExecuteAsync(async token =>
        {
            await writer.AddAsync(PilotEventType, PilotSchemaVersion,
                new IdentityOrganizationUnitChangedIntegrationEvent(tenantId, ids.NewId(), "shadow-pilot", true, 1, now),
                IntegrationEventMetadata.Create($"tenant:{tenantId:D}", "fullnet.messaging.pilot.tests"), token);
            // 只确认本代夹具事件，显式模拟 Legacy 已投递；其余待处理事件不能被测试支撑吞掉。
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("messaging.tests.confirm_fixture", """
                UPDATE fn_outbox_message SET ProcessedAtUtc = @Now
                WHERE Id IN (SELECT Id FROM fn_messaging_shadow_event WHERE CaptureGeneration = @Generation)
                """, SqlDataScope.Global), new Dictionary<string, object?> { ["Now"] = now, ["Generation"] = started.Value!.Generation }, token);
            return 0;
        }, CancellationToken.None);
        var proof = await scope.ServiceProvider.GetRequiredService<DeliveryCutoverEvidenceService>().VerifyAsync(
            new(PilotEventType, PilotSchemaVersion, "test fixture: trusted shadow and readiness port"), CancellationToken.None);
        Assert.IsTrue(proof.IsSuccess, proof.Error?.Message);
    }

    private sealed class PilotCutoverProbe : IEventDeliveryCutoverProbe
    {
        public Task<string?> ReadTargetFingerprintAsync(string eventType, int schemaVersion, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("pilot-test-resource");
        public Task<string?> CompareShadowAsync(string eventType, int schemaVersion, IReadOnlyList<CutoverShadowEvent> expected,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>("pilot-test-shadow");
    }

    /// <summary>替代控制面也执行持久意图协议；不能用测试替身绕过生产状态检查。</summary>
    private sealed class JournaledTestReadinessReader(IEventDeliveryRollbackReadinessReader inner, IEventDeliveryRollbackJournal journal)
        : IEventDeliveryRollbackReadinessReader
    {
        public async Task<EventDeliveryRollbackReadiness> PrepareAsync(string eventType, int schemaVersion, Guid generation,
            CancellationToken cancellationToken = default)
        {
            Assert.IsTrue(await journal.BindResourcesAsync(generation, "{\"testDouble\":true}", cancellationToken));
            Assert.IsTrue(await journal.TransitionAsync(generation, EventDeliveryRollbackPhase.Prepared, EventDeliveryRollbackPhase.PauseRequested, cancellationToken));
            var proof = await inner.PrepareAsync(eventType, schemaVersion, generation, cancellationToken);
            if (proof.ConnectorStopped)
                Assert.IsTrue(await journal.TransitionAsync(generation, EventDeliveryRollbackPhase.PauseRequested, EventDeliveryRollbackPhase.Paused, cancellationToken));
            return proof;
        }
        public Task AbortAsync(string eventType, int schemaVersion, Guid generation, CancellationToken cancellationToken = default) =>
            inner.AbortAsync(eventType, schemaVersion, generation, cancellationToken);
    }

    internal static AsyncServiceScope CreateHostScope(IServiceProvider provider)
    {
        var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        return scope;
    }

    // 直接调用服务的测试不经过 HTTP 管线；使用固定上下文，避免异步建容器后 AsyncLocal 身份丢失。
    private sealed class TestOperatorContextAccessor : Microsoft.AspNetCore.Http.IHttpContextAccessor
    {
        public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }
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
