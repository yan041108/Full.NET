extern alias workerhost;

using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Confluent.Kafka;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using OutboxProcessor = workerhost::Full.NET.Host.Worker.OutboxProcessor;
using OutboxWorkerOptions = workerhost::Full.NET.Host.Worker.OutboxWorkerOptions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>第一批真实纵向验证：Worker 失败、Broker DLQ、持久投影、权限恢复、幂等和审计。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MessagingBatchOneTests
{
    private const string LegacyType = "fullnet.messaging.batch.legacy";
    private const string KafkaType = "fullnet.messaging.batch.kafka";

    [TestMethod]
    public async Task SqlServer_real_failures_are_queryable_and_recoverable_with_audit() =>
        await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

    [TestMethod]
    public async Task MySql_real_failures_are_queryable_and_recoverable_with_audit() =>
        await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        var state = new FailureState();
        using var factory = new FullNetApiFactory(provider, connectionString,
            configureTestServices: services =>
            {
                services.AddSingleton(state);
                services.AddSingleton(IntegrationEventTopicDefinition.Create(state.Topic, KafkaType, 1, EventDeliveryOwner.CdcKafka));
                services.AddSingleton(IntegrationEventTopicDefinition.Create(state.LegacyTopic, LegacyType, 1, EventDeliveryOwner.LegacyPolling));
                services.AddScoped<IIntegrationEventSubscription, FailingSubscription>();
                services.AddScoped<IIntegrationEventHandler, FailingLegacyHandler>();
                services.AddScoped<ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>, SwitchableAuditWriter>();
            }, includeEnterpriseRequest: true);
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var token = await factory.CreateHostAccessTokenAsync([
            MessagingPermissions.DeadLettersRead, MessagingPermissions.DeadLettersReplay, MessagingPermissions.LegacyDeadLettersRequeue]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var tokenPayload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        using var tokenDocument = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(tokenPayload.PadRight((tokenPayload.Length + 3) / 4 * 4, '=')));
        var actorId = Guid.Parse(tokenDocument.RootElement.GetProperty("sub").GetString()!);
        await VerifyOpenApiAsync(client);
        var options = factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        await using var connection = Connection(options);
        await connection.OpenAsync();
        await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "CREATE TABLE fn_messaging_test_effect (MessageId uniqueidentifier NOT NULL PRIMARY KEY NONCLUSTERED)"
            : "CREATE TABLE fn_messaging_test_effect (MessageId BINARY(16) NOT NULL PRIMARY KEY)");
        await SeedOwnerAsync(connection, provider, state.Topic, state.LegacyTopic);

        // Legacy 失败由真实 Worker 产生；运维回队不覆盖消息正文或 Attempts。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async ct =>
            {
                await scope.ServiceProvider.GetRequiredService<IOutboxWriter>().AddAsync(LegacyType, 1,
                    new MessagingOutboxTestSupport.MessagingOutboxTestPayload("legacy-recovery"), ct);
                return true;
            }, CancellationToken.None);
        }
        using var legacyWorker = new OutboxProcessor(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<IClock>(), Options.Create(new OutboxWorkerOptions { MaxAttempts = 1 }),
            NullLogger<OutboxProcessor>.Instance);
        await legacyWorker.ProcessOnceAsync(CancellationToken.None);
        var legacyPage = await PageAsync(client, "outbox", LegacyType);
        var dead = legacyPage.Items.Single();
        var payloadBefore = await connection.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_outbox_message WHERE Id = @Id", new { dead.Id });
        Assert.AreEqual(1, dead.Attempts);
        using (var denied = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/messaging/outbox/dead-letters/{dead.Id}/requeue")
        {
            Content = JsonContent.Create(new RecoverDeadLetterRequest("repair")),
        })
        {
            denied.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateHostAccessTokenAsync([MessagingPermissions.DeadLettersRead]));
            using var response = await client.SendAsync(denied);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using (var response = await RecoverAsync(client, "outbox", dead.Id, " ")) Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await RecoverAsync(client, "outbox", dead.Id, new string(' ', 512) + "a"))
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        await connection.ExecuteAsync("UPDATE fn_messaging_stream_ownership SET CurrentOwner = 2 WHERE MessageType = @Type", new { Type = LegacyType });
        using (var response = await RecoverAsync(client, "outbox", dead.Id, "reject revoked legacy owner"))
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        await connection.ExecuteAsync("UPDATE fn_messaging_stream_ownership SET CurrentOwner = 0 WHERE MessageType = @Type", new { Type = LegacyType });
        state.FailAudit = true;
        using (var response = await RecoverAsync(client, "outbox", dead.Id, "verify audit rollback"))
            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.AreEqual(1L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_outbox_message WHERE Id = @Id AND DeadLetteredAtUtc IS NOT NULL", new { dead.Id }));
        state.FailAudit = false;
        var attempts = await Task.WhenAll(RecoverAsync(client, "outbox", dead.Id, "repair verified legacy failure"),
            RecoverAsync(client, "outbox", dead.Id, "concurrent retry"));
        Assert.AreEqual(1, attempts.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.IsTrue(attempts.All(r => r.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound or HttpStatusCode.Conflict));
        foreach (var response in attempts) response.Dispose();
        Assert.AreEqual(1, await connection.QuerySingleAsync<int>("SELECT Attempts FROM fn_outbox_message WHERE Id = @Id", new { dead.Id }));
        CollectionAssert.AreEqual(payloadBefore, await connection.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_outbox_message WHERE Id = @Id", new { dead.Id }));
        state.FailLegacy = false;
        await legacyWorker.ProcessOnceAsync(CancellationToken.None);
        Assert.AreEqual(1, state.LegacyHandled);
        Assert.AreEqual(0L, (await PageAsync(client, "outbox", LegacyType)).Total);
        await VerifyAuditAsync(connection, dead.Id, "messaging.outbox_dead_letters.requeue", actorId);

        // 真正运行生产 Kafka 处理器和 DLQ 投影 Worker；没有手工插入 Inbox failed 行。
        var broker = await KafkaFixture.GetOrStartAsync();
        string[] topics;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IIntegrationEventSubscriptionCatalog>();
            topics = scope.ServiceProvider.GetServices<IIntegrationEventSubscription>()
                .Select(s => catalog.GetTopicRequired(s.EventType, s.SchemaVersion).TopicCode).Distinct().ToArray();
        }
        await broker.EnsureTopicsAsync(topics.SelectMany(t => new[] { t, t + ".dlq", t + ".retry.5s", t + ".retry.1m", t + ".retry.15m" }).ToArray());
        var kafkaOptions = broker.CreateOptions("messaging-batch-" + Guid.NewGuid().ToString("N"));
        kafkaOptions.DeadLetterProjectionGroupId = "fullnet.batch.projection." + Guid.NewGuid().ToString("N");
        var wrapped = Options.Create(kafkaOptions);
        using var producer = broker.CreateProducer("fullnet.batch.producer");
        using var runtimeProducer = new KafkaMessagingProducer(wrapped);
        var processor = new KafkaConsumerMessageProcessor(wrapped, factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new KafkaEnvelopeReader(), new KafkaOffsetCommitter(), new KafkaFailureClassifier(),
            new KafkaRetryRouter(wrapped, runtimeProducer, NullLogger<KafkaRetryRouter>.Instance),
            new KafkaDeadLetterPublisher(wrapped, runtimeProducer, NullLogger<KafkaDeadLetterPublisher>.Instance),
            NullLogger<KafkaConsumerMessageProcessor>.Instance);
        using var consumerWorker = new KafkaConsumerWorker(wrapped, factory.Services.GetRequiredService<IServiceScopeFactory>(),
            processor, NullLogger<KafkaConsumerWorker>.Instance);
        using var projectionWorker = new KafkaDeadLetterProjectionWorker(wrapped, factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new KafkaEnvelopeReader(), NullLogger<KafkaDeadLetterProjectionWorker>.Instance);
        await consumerWorker.StartAsync(CancellationToken.None);
        await projectionWorker.StartAsync(CancellationToken.None);
        try
        {
            var messageId = Guid.CreateVersion7();
            var headers = Headers(messageId);
            headers.Add(KafkaEnvelopeHeaderNames.TenantId, Encoding.UTF8.GetBytes(state.TenantId.ToString()));
            await producer.ProduceAsync(state.Topic, new Message<string, byte[]> { Key = "batch-key", Value = [1, 2, 3], Headers = headers });
            OperationalDeadLetterResponse? projected = null;
            await WaitAsync(async () =>
            {
                projected = (await PageAsync(client, "kafka", KafkaType)).Items.SingleOrDefault(r => r.MessageId == messageId);
                return projected is not null;
            });
            Assert.IsNotNull(projected);
            Assert.IsTrue(projected.CanReplay);
            Assert.AreEqual(state.TenantId, projected.TenantId);
            Assert.AreEqual(0L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_messaging_test_effect"));
            Assert.AreEqual(0L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE MessageId = @messageId", new { messageId }));
            using (var detail = await client.GetAsync($"/api/v1/messaging/kafka/dead-letters/{projected.Id}"))
            {
                var json = await detail.Content.ReadAsStringAsync();
                Assert.IsFalse(json.Contains("snapshotJson", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(json.Contains("rawPayload", StringComparison.OrdinalIgnoreCase));
            }
            state.FailKafka = false;
            state.FailAudit = true;
            using (var response = await RecoverAsync(client, "kafka", projected.Id, "verify replay audit rollback"))
                Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.AreEqual(0L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_messaging_test_effect"));
            Assert.IsNull((await PageAsync(client, "kafka", KafkaType)).Items.Single().ReplayedAtUtc);
            state.FailAudit = false;
            using (var recovered = await RecoverAsync(client, "kafka", projected.Id, "repair verified broker failure"))
                Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode, await recovered.Content.ReadAsStringAsync());
            using (var repeated = await RecoverAsync(client, "kafka", projected.Id, "verify inbox duplicate"))
            {
                Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode);
                Assert.AreEqual("already_processed", (await repeated.Content.ReadFromJsonAsync<RecoverDeadLetterResponse>())!.Outcome);
            }
            Assert.AreEqual(1L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_messaging_test_effect"));
            Assert.AreEqual(state.TenantId, state.ObservedTenantId);
            await VerifyAuditAsync(connection, projected.Id, "messaging.dead_letters.replay", actorId);

            // 缺失 EventId 的真实 Broker 记录也必须进入投影，但恢复接口明确拒绝执行。
            headers.Remove(KafkaEnvelopeHeaderNames.EventId);
            await producer.ProduceAsync(state.Topic, new Message<string, byte[]> { Key = "broken", Value = [4], Headers = headers });
            OperationalDeadLetterResponse? broken = null;
            await WaitAsync(async () =>
            {
                broken = (await PageAsync(client, "kafka")).Items.SingleOrDefault(r => !r.CanReplay && r.DlqTopic == state.Topic + ".dlq");
                return broken is not null;
            });
            using var rejected = await RecoverAsync(client, "kafka", broken!.Id, "inspect malformed source");
            Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode);

            // 重读真实 DLQ 并再次提交同一位置，不能覆盖已恢复状态或创建第二条记录。
            using var dlqConsumer = broker.CreateConsumer("fullnet.batch.inspect." + Guid.NewGuid().ToString("N"), "batch-inspect");
            dlqConsumer.Subscribe(state.Topic + ".dlq");
            var original = await KafkaTestMessages.ConsumeOneAsync(dlqConsumer, TimeSpan.FromSeconds(30));
            await using var captureScope = factory.Services.CreateAsyncScope();
            captureScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            await captureScope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterSink>()
                .CaptureAsync(KafkaDeadLetterCapture.Create(original, new KafkaEnvelopeReader()), CancellationToken.None);
            var after = await PageAsync(client, "kafka", KafkaType);
            Assert.AreEqual(1L, after.Total);
            Assert.IsNotNull(after.Items.Single().ReplayedAtUtc);
            var root = new DirectoryInfo(FullNetApiFactory.FindRepositoryRoot());
            var migration = await File.ReadAllTextAsync(Path.Combine(root.FullName, "src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Migrations",
                provider == DatabaseProvider.SqlServer ? "SqlServer" : "MySql", "287_MessagingDeadLetterProjection.sql"));
            if (provider == DatabaseProvider.SqlServer)
                await connection.ExecuteAsync("DROP INDEX IX_fn_messaging_dead_letter_LastFailedAtUtc_Id ON fn_messaging_dead_letter");
            await connection.ExecuteAsync(migration);
            await connection.ExecuteAsync(migration);
            Assert.AreEqual(2L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_messaging_dead_letter"));
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await projectionWorker.StopAsync(stop.Token);
            await consumerWorker.StopAsync(stop.Token);
        }
    }

    private static async Task VerifyAuditAsync(DbConnection connection, Guid id, string action, Guid actorId)
    {
        var audits = (await connection.QueryAsync<AuditRow>("SELECT ActorUserId, ActorDisplayName, DiffSummaryJson FROM fn_messaging_domain_audit WHERE EntityId = @id AND ActionKey = @action", new { id, action })).ToArray();
        Assert.IsNotEmpty(audits);
        Assert.IsTrue(audits.All(a => a.ActorUserId == actorId && !string.IsNullOrWhiteSpace(a.ActorDisplayName)
            && a.DiffSummaryJson.Contains("reason", StringComparison.Ordinal)));
    }

    private static async Task VerifyOpenApiAsync(HttpClient client)
    {
        var json = await client.GetStringAsync("/openapi/v1.json");
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");
        foreach (var source in new[] { "outbox", "kafka" })
        {
            var prefix = source == "outbox" ? "messagingLegacy" : "messagingKafka";
            var route = "/api/v1/messaging/" + source + "/dead-letters";
            Assert.AreEqual(prefix + "ListDeadLetters", paths.GetProperty(route).GetProperty("get").GetProperty("operationId").GetString());
            Assert.AreEqual(prefix + "GetDeadLetter", paths.GetProperty(route + "/{id}").GetProperty("get").GetProperty("operationId").GetString());
            Assert.AreEqual(prefix + "RecoverDeadLetter", paths.GetProperty(route + "/{id}/" + (source == "outbox" ? "requeue" : "replay"))
                .GetProperty("post").GetProperty("operationId").GetString());
        }
        var export = Environment.GetEnvironmentVariable("FULLNET_CLIENT_OPENAPI_EXPORT_PATH");
        if (!string.IsNullOrWhiteSpace(export)) await File.WriteAllTextAsync(export, json);
    }

    private static DbConnection Connection(DatabaseOptions options) => options.Provider == DatabaseProvider.SqlServer
        ? new SqlConnection(options.ConnectionString) : new MySqlConnection(MySqlConnectionStringPolicy.Create(options.ConnectionString,
            MySqlGuidStorageMode.Binary16, allowUserVariables: false));

    private static Task SeedOwnerAsync(DbConnection connection, DatabaseProvider provider, string topic, string legacyTopic) => connection.ExecuteAsync(
        "INSERT INTO fn_messaging_stream_ownership (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner, CutoffEventId, CutoffOccurredAtUtc, Reason, CreatedAtUtc, UpdatedAtUtc) VALUES (@Type, 1, @Topic, @Owner, 0, @Empty, @Now, @Reason, @Now, @Now)",
        new[] { new { Type = KafkaType, Topic = topic, Owner = 2, Empty = Guid.CreateVersion7(), Now = DateTime.UtcNow, Reason = "batch-one real failure test" },
            new { Type = LegacyType, Topic = legacyTopic, Owner = 0, Empty = Guid.CreateVersion7(), Now = DateTime.UtcNow, Reason = "batch-one real failure test" } });

    private static async Task<PagedResult<OperationalDeadLetterResponse>> PageAsync(HttpClient client, string source, string? type = null)
    {
        using var response = await client.GetAsync($"/api/v1/messaging/{source}/dead-letters?page=1&pageSize=100" + (type is null ? "" : "&messageType=" + Uri.EscapeDataString(type)));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PagedResult<OperationalDeadLetterResponse>>())!;
    }

    private static Task<HttpResponseMessage> RecoverAsync(HttpClient client, string source, Guid id, string reason) =>
        client.PostAsJsonAsync($"/api/v1/messaging/{source}/dead-letters/{id}/{(source == "outbox" ? "requeue" : "replay")}", new RecoverDeadLetterRequest(reason));

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (!await condition()) await Task.Delay(200, timeout.Token);
    }

    private static Headers Headers(Guid id)
    {
        var headers = new Headers();
        foreach (var (key, value) in new Dictionary<string, string>
        {
            [KafkaEnvelopeHeaderNames.EventId] = id.ToString(), [KafkaEnvelopeHeaderNames.MessageType] = KafkaType,
            [KafkaEnvelopeHeaderNames.SchemaVersion] = "1", [KafkaEnvelopeHeaderNames.ContentType] = "application/x-memorypack",
            [KafkaEnvelopeHeaderNames.Producer] = "fullnet.batch", [KafkaEnvelopeHeaderNames.OccurredAtUtc] = DateTimeOffset.UtcNow.ToString("O"),
        }) headers.Add(key, Encoding.UTF8.GetBytes(value));
        return headers;
    }

    private sealed class FailureState
    {
        public volatile bool FailLegacy = true;
        public volatile bool FailKafka = true;
        public volatile bool FailAudit;
        public int LegacyHandled { get; set; }
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid? ObservedTenantId { get; set; }
        public string Topic { get; } = "fullnet.batch.test.run" + Guid.NewGuid().ToString("N") + ".v1";
        public string LegacyTopic => Topic[..^3] + ".legacy.v1";
        public string Consumer { get; } = "fullnet.batch.consumer.run" + Guid.NewGuid().ToString("N");
    }

    private sealed class FailingLegacyHandler(FailureState state) : IIntegrationEventHandler
    {
        public string EventType => LegacyType;
        public int SchemaVersion => 1;
        public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
        public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            if (state.FailLegacy) throw new IOException("simulated legacy dependency failure");
            state.LegacyHandled++;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSubscription(FailureState state, ICommandExecutor command, ICurrentTenant tenant) : IIntegrationEventSubscription
    {
        public string ConsumerName => state.Consumer;
        public string EventType => KafkaType;
        public int SchemaVersion => 1;
        public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.MessageIdDeduplication;
        public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            Assert.AreEqual(context.TenantId, tenant.Id);
            state.ObservedTenantId = tenant.Id;
            await command.ExecuteAsync(new SqlStatement("messaging.test.effect", "INSERT INTO fn_messaging_test_effect (MessageId) VALUES (@Id)", SqlDataScope.Global),
                new Dictionary<string, object?> { ["Id"] = context.MessageId }, cancellationToken);
            if (state.FailKafka) throw new IntegrationEventPermanentException(new IntegrationEventFailure(
                IntegrationEventFailureKind.Business, "messaging.business.batch_test", "Simulated permanent failure."));
        }
    }

    private sealed record AuditRow(Guid? ActorUserId, string ActorDisplayName, string DiffSummaryJson);

    private sealed class SwitchableAuditWriter(ICommandExecutor command, IIdGenerator ids, IClock clock, FailureState state)
        : ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>
    {
        private readonly MessagingDomainAuditWriter _writer = new(command, ids, clock);
        public Task WriteAsync(MessagingDomainAuditWrite value, CancellationToken cancellationToken) => state.FailAudit
            ? throw new InvalidOperationException("Injected audit persistence failure.")
            : _writer.WriteAsync(value, cancellationToken);
    }
}
