using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>Host 消息运维纵向切片：权限、死信重放幂等与切换前置校验。</summary>
internal static class MessagingOperationsAssertions
{
    internal const string LegacyEventType = "fullnet.messaging.ops.legacy.event";
    internal const string LegacyTopicCode = "messaging.ops-legacy.v1";

    public static void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton(
            IntegrationEventTopicDefinition.Create(
                MessagingInboxTestSupport.TopicCode,
                MessagingOutboxTestSupport.TestEventType,
                MessagingOutboxTestSupport.TestSchemaVersion,
                EventDeliveryOwner.CdcKafka));
        services.AddSingleton(
            IntegrationEventTopicDefinition.Create(
                LegacyTopicCode,
                LegacyEventType,
                MessagingOutboxTestSupport.TestSchemaVersion,
                EventDeliveryOwner.LegacyPolling));
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IIntegrationEventSubscription, OpsNoOpSubscription>());
        services.RemoveAll<IntegrationEventSubscriptionCatalog>();
        services.AddSingleton(provider =>
            new IntegrationEventSubscriptionCatalog(
                provider.GetServices<IntegrationEventTopicDefinition>(),
                provider.GetServices<IIntegrationEventSubscription>()));
    }

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyReplayLifecycleAndAuditAsync(factory, client, cancellationToken);
        await VerifyCutoverPreconditionsAsync(factory, client, cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/messaging/dead-letters?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task VerifyReplayLifecycleAndAuditAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var eventId = await SeedFailedDeadLetterAsync(factory, cancellationToken);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/messaging/dead-letters?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<DeadLetterResponse>>(
            cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.MessageId == eventId));

        var auditBefore = await CountDomainAuditRowsAsync(factory, cancellationToken);
        using var replayRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/messaging/dead-letters/replay",
            adminToken,
            new ReplayDeadLetterRequest(
                MessagingInboxTestSupport.ConsumerName,
                eventId));
        using var replayResponse = await client.SendAsync(replayRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, replayResponse.StatusCode);
        var replayed = await replayResponse.Content.ReadFromJsonAsync<DeadLetterReplayResponse>(
            cancellationToken);
        Assert.IsNotNull(replayed);
        Assert.AreEqual(DeadLetterReplayOutcomes.Processed, replayed.Outcome);
        Assert.IsGreaterThan(
            auditBefore,
            await CountDomainAuditRowsAsync(factory, cancellationToken));

        using var duplicateReplay = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/messaging/dead-letters/replay",
            adminToken,
            new ReplayDeadLetterRequest(
                MessagingInboxTestSupport.ConsumerName,
                eventId));
        using var duplicateResponse = await client.SendAsync(duplicateReplay, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, duplicateResponse.StatusCode);
        var duplicate = await duplicateResponse.Content.ReadFromJsonAsync<DeadLetterReplayResponse>(
            cancellationToken);
        Assert.IsNotNull(duplicate);
        Assert.AreEqual(DeadLetterReplayOutcomes.AlreadyProcessed, duplicate.Outcome);

        using var unknownRouteReplay = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/messaging/dead-letters/replay",
            adminToken,
            new ReplayDeadLetterRequest("fullnet.messaging.unknown.consumer", eventId));
        using var unknownRouteResponse = await client.SendAsync(unknownRouteReplay, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, unknownRouteResponse.StatusCode);
    }

    private static async Task VerifyCutoverPreconditionsAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        using var statusRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/messaging/delivery-status");
        statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var statusResponse = await client.SendAsync(statusRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, statusResponse.StatusCode);

        using var invalidTarget = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/messaging/delivery/cutover",
            adminToken,
            new ChangeDeliveryOwnerRequest(
                LegacyEventType,
                MessagingOutboxTestSupport.TestSchemaVersion,
                EventDeliveryOwner.ShadowCdc,
                "invalid-target"));
        using var invalidTargetResponse = await client.SendAsync(invalidTarget, cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, invalidTargetResponse.StatusCode);

        await SeedLegacyPendingOutboxAsync(factory, cancellationToken);
        using var backlogBlocked = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/messaging/delivery/cutover",
            adminToken,
            new ChangeDeliveryOwnerRequest(
                LegacyEventType,
                MessagingOutboxTestSupport.TestSchemaVersion,
                EventDeliveryOwner.CdcKafka,
                "blocked-by-backlog"));
        using var backlogResponse = await client.SendAsync(backlogBlocked, cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, backlogResponse.StatusCode);

        using var problem = JsonDocument.Parse(
            await backlogResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            MessagingErrorCodes.LegacyBacklogNotDrained,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<Guid> SeedFailedDeadLetterAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.CreateVersion7();
        await using var scope = factory.Services.CreateAsyncScope();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenantAccessor.SetHost();
        var serializer = scope.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>();
        var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var payload = new MessagingOutboxTestSupport.MessagingOutboxTestPayload("ops-replay");
        var payloadBytes = serializer.Serialize(payload);
        var metadata = MessagingOutboxTestSupport.CreateMetadata($"ops-{eventId:N}");
        await writer.AddAsync(
            MessagingOutboxTestSupport.TestEventType,
            MessagingOutboxTestSupport.TestSchemaVersion,
            payload,
            metadata,
            cancellationToken);

        var hash = SHA256.HashData(payloadBytes);
        if (factory.Provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(
                factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString);
            await connection.ExecuteAsync(
                """
                INSERT INTO dbo.fn_messaging_inbox_message
                    (ConsumerName, MessageId, MessageType, SchemaVersion, TenantId,
                     PayloadHash, Status, Attempts, ReceivedAtUtc, LastErrorCode, LastError)
                VALUES
                    (@ConsumerName, @MessageId, @MessageType, @SchemaVersion, NULL,
                     @PayloadHash, 'failed', 1, SYSDATETIMEOFFSET(), @LastErrorCode, @LastError)
                """,
                new
                {
                    ConsumerName = MessagingInboxTestSupport.ConsumerName,
                    MessageId = eventId,
                    MessageType = MessagingOutboxTestSupport.TestEventType,
                    SchemaVersion = MessagingOutboxTestSupport.TestSchemaVersion,
                    PayloadHash = hash,
                    LastErrorCode = "messaging.transient.test",
                    LastError = "Injected dead letter for ops replay.",
                });
        }
        else
        {
            await using var connection = new MySqlConnection(
                MySqlConnectionStringPolicy.Create(
                    factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: false));
            await connection.ExecuteAsync(
                """
                INSERT INTO fn_messaging_inbox_message
                    (ConsumerName, MessageId, MessageType, SchemaVersion, TenantId,
                     PayloadHash, Status, Attempts, ReceivedAtUtc, LastErrorCode, LastError)
                VALUES
                    (@ConsumerName, @MessageId, @MessageType, @SchemaVersion, NULL,
                     @PayloadHash, 'failed', 1, UTC_TIMESTAMP(6), @LastErrorCode, @LastError)
                """,
                new
                {
                    ConsumerName = MessagingInboxTestSupport.ConsumerName,
                    MessageId = eventId,
                    MessageType = MessagingOutboxTestSupport.TestEventType,
                    SchemaVersion = MessagingOutboxTestSupport.TestSchemaVersion,
                    PayloadHash = hash,
                    LastErrorCode = "messaging.transient.test",
                    LastError = "Injected dead letter for ops replay.",
                });
        }

        if (factory.Provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(
                factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString);
            await connection.ExecuteAsync(
                "UPDATE dbo.fn_messaging_outbox_event SET Id = @EventId WHERE PartitionKey = @PartitionKey",
                new { EventId = eventId, PartitionKey = $"ops-{eventId:N}" });
        }
        else
        {
            await using var connection = new MySqlConnection(
                MySqlConnectionStringPolicy.Create(
                    factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: false));
            await connection.ExecuteAsync(
                "UPDATE fn_messaging_outbox_event SET Id = @EventId WHERE PartitionKey = @PartitionKey",
                new { EventId = eventId, PartitionKey = $"ops-{eventId:N}" });
        }

        return eventId;
    }

    private static async Task SeedLegacyPendingOutboxAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        if (factory.Provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(
                factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString);
            await connection.ExecuteAsync(
                """
                INSERT INTO dbo.fn_outbox_message
                    (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId, Payload,
                     OccurredAtUtc, ProcessedAtUtc, NextAttemptAtUtc, Attempts, LockId, LockedUntilUtc, Error)
                VALUES
                    (@Id, @MessageType, @SchemaVersion, 'application/x-msgpack', NULL, NULL, 0x01,
                     SYSDATETIMEOFFSET(), NULL, SYSDATETIMEOFFSET(), 0, NULL, NULL, NULL)
                """,
                new
                {
                    Id = id,
                    MessageType = LegacyEventType,
                    SchemaVersion = MessagingOutboxTestSupport.TestSchemaVersion,
                });
        }
        else
        {
            await using var connection = new MySqlConnection(
                MySqlConnectionStringPolicy.Create(
                    factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: false));
            await connection.ExecuteAsync(
                """
                INSERT INTO fn_outbox_message
                    (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId, Payload,
                     OccurredAtUtc, ProcessedAtUtc, NextAttemptAtUtc, Attempts, LockId, LockedUntilUtc, Error)
                VALUES
                    (@Id, @MessageType, @SchemaVersion, 'application/x-msgpack', NULL, NULL, 0x01,
                     UTC_TIMESTAMP(6), NULL, UTC_TIMESTAMP(6), 0, NULL, NULL, NULL)
                """,
                new
                {
                    Id = id,
                    MessageType = LegacyEventType,
                    SchemaVersion = MessagingOutboxTestSupport.TestSchemaVersion,
                });
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task<long> CountDomainAuditRowsAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        return await executor.QuerySingleOrDefaultAsync<long>(
            Full.NET.Modules.Messaging.Persistence.MessagingOperationsSql.CountDomainAuditRows,
            cancellationToken: cancellationToken);
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }

    private sealed class OpsNoOpSubscription : IIntegrationEventSubscription
    {
        public string ConsumerName => MessagingInboxTestSupport.ConsumerName;

        public string EventType => MessagingOutboxTestSupport.TestEventType;

        public int SchemaVersion => MessagingOutboxTestSupport.TestSchemaVersion;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
