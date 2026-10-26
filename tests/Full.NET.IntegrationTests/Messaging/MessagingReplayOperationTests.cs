using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using Dapper;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Features.ReplayKafkaRange;
using Full.NET.Modules.Messaging.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>真实 Broker、Inbox 与双库控制日志；仅检查点/响应丢失通过明确故障注入产生。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MessagingReplayOperationTests
{
    private const string EventType = "fullnet.messaging.replay.operation.test";
    [TestMethod]
    public async Task SqlServer_replay_progress_survives_partial_unknown_cancel_and_lost_commit() =>
        await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task MySql_replay_progress_survives_partial_unknown_cancel_and_lost_commit() =>
        await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider database, string connectionString)
    {
        var kafka = await KafkaFixture.GetOrStartAsync();
        var state = new Faults(); await kafka.EnsureTopicsAsync(state.Topic);
        using var factory = new FullNetApiFactory(database, connectionString, configureTestServices: services =>
        {
            services.AddSingleton(state);
            services.AddSingleton(new KafkaReplayExecutionPolicy(true, 100, TimeSpan.FromSeconds(30)));
            services.AddSingleton(IntegrationEventTopicDefinition.Create(state.Topic, EventType, 1, EventDeliveryOwner.CdcKafka));
            services.AddScoped<IIntegrationEventSubscription, EffectSubscription>();
            services.RemoveAll<ITrackedKafkaReplayService>();
            services.AddScoped<ITrackedKafkaReplayService>(provider => new FaultTracked(new KafkaReplayService(
                provider.GetRequiredService<IIntegrationEventSubscriptionCatalog>(), new KafkaReplayConsumerFactory(Options.Create(kafka.CreateOptions("fullnet.replay.operation.tests"))),
                new KafkaReplayMessageProcessor(provider.GetRequiredService<IServiceScopeFactory>(), new KafkaEnvelopeReader()),
                Options.Create(kafka.CreateOptions("fullnet.replay.operation.tests"))), state));
            services.AddScoped<ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>, FaultAudit>();
            var createTransaction = services.Last(item => item.ServiceType == typeof(ICommandTransaction)).ImplementationFactory!;
            services.Replace(ServiceDescriptor.Scoped<ICommandTransaction>(provider => new LostCommit((ICommandTransaction)createTransaction(provider), state)));
        });
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateHostAccessTokenAsync([
            MessagingPermissions.EventsRead, MessagingPermissions.KafkaRangeReplay]));
        var captureRead = await client.GetAsync("/api/v1/messaging/delivery/shadow-captures/current?eventType=" + EventType + "&schemaVersion=1");
        Assert.AreEqual(HttpStatusCode.NotFound, captureRead.StatusCode);
        Assert.AreEqual("messaging.shadow_capture.not_found", JsonDocument.Parse(await captureRead.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
        var forbiddenCapture = await client.PostAsJsonAsync("/api/v1/messaging/delivery/shadow-captures/", new
        { requestId = Guid.CreateVersion7(), expectedGeneration = (Guid?)null, eventType = EventType, schemaVersion = 1, maxEvents = 1, windowSeconds = 1, reason = "read permission is insufficient" });
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenCapture.StatusCode);
        await using var connection = Open(database, connectionString); await connection.OpenAsync();
        await connection.ExecuteAsync(database == DatabaseProvider.SqlServer
            ? "CREATE TABLE fn_messaging_test_effect (MessageId uniqueidentifier NOT NULL PRIMARY KEY NONCLUSTERED)"
            : "CREATE TABLE fn_messaging_test_effect (MessageId BINARY(16) NOT NULL PRIMARY KEY)");
        await connection.ExecuteAsync("""
            INSERT INTO fn_messaging_stream_ownership (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner, CutoffEventId, CutoffOccurredAtUtc, Reason, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@EventType, 1, @Topic, 2, 0, @Id, @Now, 'replay operation integration', @Now, @Now)
            """, new { EventType, state.Topic, Id = Guid.CreateVersion7(), Now = DateTime.UtcNow });
        using var producer = kafka.CreateProducer("fullnet.replay.operation.tests");
        async Task<long> Produce()
        {
            var delivery = await producer.ProduceAsync(new TopicPartition(state.Topic, 0), KafkaTestMessages.Create(state.Topic,
                Guid.CreateVersion7().ToString(), [1], EventType, Guid.CreateVersion7()));
            return delivery.Offset.Value;
        }
        async Task<KafkaReplayOperationResponse> Create(long start, long end, Guid? key = null)
        {
            var request = new CreateKafkaReplayOperationRequest(key ?? Guid.CreateVersion7(), state.Topic, null, null,
                start.ToString(System.Globalization.CultureInfo.InvariantCulture), end.ToString(System.Globalization.CultureInfo.InvariantCulture), [0], state.Consumer, 1, "repair verified operation");
            using var response = await client.PostAsJsonAsync(Route, request);
            return await Read<KafkaReplayOperationResponse>(response, HttpStatusCode.Created);
        }
        async Task<KafkaReplayOperationResponse> Get(Guid id) => await client.GetFromJsonAsync<KafkaReplayOperationResponse>($"{Route}/{id}") ?? throw new AssertFailedException("缺少操作记录。");
        async Task<KafkaReplayOperationResponse> Execute(Guid id)
        {
            using var response = await client.PostAsJsonAsync($"{Route}/{id}/execute", new ChangeKafkaReplayOperationRequest("resume verified fixed window"));
            return await Read<KafkaReplayOperationResponse>(response, HttpStatusCode.OK);
        }

        // 固定两个真实位点；单次预算 1，恢复不得重新扫描已确认的第一条。
        var first = await Produce(); var second = await Produce(); var key = Guid.CreateVersion7();
        var operation = await Create(first, second, key); Assert.AreEqual("prepared", operation.State);
        Assert.AreEqual(operation.OperationId, (await Create(first, second, key)).OperationId);
        using (var conflict = await client.PostAsJsonAsync(Route, new CreateKafkaReplayOperationRequest(key, state.Topic, null, null,
                   first.ToString(), second.ToString(), [0], state.Consumer, 2, "repair verified operation"))) Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        operation = await Execute(operation.OperationId); Assert.AreEqual("partial", operation.State); Assert.AreEqual(1L, operation.ConfirmedMessages);
        operation = await Execute(operation.OperationId); Assert.AreEqual("completed", operation.State); Assert.AreEqual(2L, operation.ConfirmedMessages);
        Assert.AreEqual(2L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_messaging_test_effect"));
        Assert.AreEqual("completed", (await Execute(operation.OperationId)).State);

        // 业务已提交，检查点故障：计数保持未确认，恢复经真实 Inbox 去重，业务行只能有一份。
        var offset = await Produce(); operation = await Create(offset, offset); state.LoseCheckpoint = true;
        using (var failure = await client.PostAsJsonAsync($"{Route}/{operation.OperationId}/execute", new ChangeKafkaReplayOperationRequest("inject checkpoint loss"))) Assert.AreEqual(HttpStatusCode.InternalServerError, failure.StatusCode);
        operation = await Get(operation.OperationId); Assert.AreEqual("result_unknown", operation.State); Assert.IsTrue(operation.HasUnconfirmedMessage); Assert.AreEqual(0L, operation.ConfirmedMessages);
        operation = await Execute(operation.OperationId); Assert.AreEqual("completed", operation.State); Assert.AreEqual(1L, operation.AlreadyProcessedMessages);
        Assert.AreEqual(3L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_messaging_test_effect"));

        // 成功终态提交后响应丢失，必须从新作用域确认成功，不能覆盖成未知。
        offset = await Produce(); operation = await Create(offset, offset); state.LoseFinalCommit = true;
        operation = await Execute(operation.OperationId); Assert.AreEqual("completed", operation.State);

        // 终态审计失败必须回滚终态；进度仍可确认，恢复空余范围后再完成，不重复业务写入。
        offset = await Produce(); operation = await Create(offset, offset); state.FailCompletionAudit = true;
        using (var failure = await client.PostAsJsonAsync($"{Route}/{operation.OperationId}/execute", new ChangeKafkaReplayOperationRequest("inject terminal audit failure"))) Assert.AreEqual(HttpStatusCode.InternalServerError, failure.StatusCode);
        operation = await Get(operation.OperationId); Assert.AreEqual("result_unknown", operation.State); Assert.AreEqual(1L, operation.ConfirmedMessages);
        state.FailCompletionAudit = false; Assert.AreEqual("completed", (await Execute(operation.OperationId)).State);

        // 真实执行锁竞争与取消；已在途的业务确认可落盘，剩余范围不能被标为完成。
        first = await Produce(); second = await Produce(); operation = await Create(first, second); state.BlockBefore = true;
        var executing = client.PostAsJsonAsync($"{Route}/{operation.OperationId}/execute", new ChangeKafkaReplayOperationRequest("exercise cancellation"));
        await state.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            using var concurrent = await client.PostAsJsonAsync($"{Route}/{operation.OperationId}/execute", new ChangeKafkaReplayOperationRequest("concurrent attempt")); Assert.AreEqual(HttpStatusCode.Conflict, concurrent.StatusCode);
            using var cancellation = await client.PostAsJsonAsync($"{Route}/{operation.OperationId}/cancel", new ChangeKafkaReplayOperationRequest("stop remaining messages"));
            Assert.AreEqual("cancel_requested", (await Read<KafkaReplayOperationResponse>(cancellation, HttpStatusCode.OK)).State);
        }
        finally { state.Release.TrySetResult(); }
        using (var finished = await executing) operation = await Read<KafkaReplayOperationResponse>(finished, HttpStatusCode.OK);
        Assert.AreEqual("cancelled", operation.State); Assert.AreEqual(1L, operation.ConfirmedMessages);
        operation = await Execute(operation.OperationId); Assert.AreEqual("completed", operation.State); Assert.AreEqual(2L, operation.ConfirmedMessages);

        // 过期/旧代检查点不能继续更新；读接口将过期运行状态标记为未知。
        offset = await Produce(); var stale = await Create(offset, offset); var staleLease = Guid.CreateVersion7();
        await connection.ExecuteAsync("UPDATE fn_messaging_replay_operation SET State = 'running', LeaseToken = @Lease, LeaseUntilUtc = @Past WHERE Id = @Id", new { Lease = staleLease, Past = DateTime.UtcNow.AddMinutes(-1), Id = stale.OperationId });
        Assert.AreEqual("result_unknown", (await Get(stale.OperationId)).State);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var progress = new KafkaReplayOperationProgress(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), stale.OperationId, staleLease, scope.ServiceProvider.GetRequiredService<IClock>());
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => progress.BindSnapshotAsync(new("cluster", "topic", [new(0, offset, offset + 1, offset)]), default));
        }
        Assert.AreEqual("completed", (await Execute(stale.OperationId)).State);

        var overview = await client.GetFromJsonAsync<DeliveryStatusResponse>("/api/v1/messaging/delivery-status"); Assert.IsNotNull(overview);
        Assert.AreEqual("not_observed", overview.TargetRuntimeState);
        var stream = overview.Streams.Single(item => item.EventType == EventType); Assert.AreEqual(0L, stream.LegacyBacklog!.PendingCount);
        CollectionAssert.Contains(stream.ConsumerNames.ToArray(), state.Consumer);
        CollectionAssert.Contains(stream.BlockingReasons.ToArray(), "target_runtime_not_observed");
        using (var forbidden = factory.CreateClientForHost("localhost"))
        {
            forbidden.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync([MessagingPermissions.EventsRead]));
            using var response = await forbidden.PostAsJsonAsync($"{Route}/{operation.OperationId}/execute", new ChangeKafkaReplayOperationRequest("verify exact permission")); Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        var sql = await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Migrations", database.ToString(), "293_MessagingReplayOperation.sql"));
        if (database == DatabaseProvider.SqlServer) await connection.ExecuteAsync("DROP INDEX IX_fn_messaging_replay_operation_CreatedAtUtc_Id ON fn_messaging_replay_operation");
        await connection.ExecuteAsync(sql); await connection.ExecuteAsync(sql);
        Assert.AreEqual(8L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_messaging_test_effect"));
        var export = Environment.GetEnvironmentVariable("FULLNET_CLIENT_OPENAPI_EXPORT_PATH");
        if (!string.IsNullOrWhiteSpace(export)) await File.WriteAllTextAsync(export, await client.GetStringAsync("/openapi/v1.json"));
    }

    private const string Route = "/api/v1/messaging/kafka/replay-operations";
    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new AssertFailedException("缺少响应。");
    }
    private static DbConnection Open(DatabaseProvider provider, string value) => provider == DatabaseProvider.SqlServer ? new SqlConnection(value)
        : new MySqlConnection(new MySqlConnectionStringBuilder(value) { GuidFormat = MySqlGuidFormat.Binary16, AllowUserVariables = true }.ConnectionString);
    private sealed class Faults
    {
        public string Topic { get; } = "messaging.replay-operation.t" + Guid.CreateVersion7().ToString("N") + ".v1";
        public string Consumer => "fullnet.messaging.replay.operation";
        public bool LoseCheckpoint, LoseFinalCommit, FailCompletionAudit, BlockBefore;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class EffectSubscription(ICommandExecutor command, Faults state) : IIntegrationEventSubscription
    {
        public string ConsumerName => state.Consumer; public string EventType => MessagingReplayOperationTests.EventType; public int SchemaVersion => 1;
        public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.MessageIdDeduplication;
        public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken token) =>
            _ = await command.ExecuteAsync(new SqlStatement("messaging.test.replay_effect", "INSERT INTO fn_messaging_test_effect (MessageId) VALUES (@Id)", SqlDataScope.Global), new Dictionary<string, object?> { ["Id"] = context.MessageId }, token);
    }
    private sealed class FaultTracked(ITrackedKafkaReplayService inner, Faults state) : ITrackedKafkaReplayService
    {
        public Task<KafkaReplayResult> ReplayTrackedAsync(KafkaReplayRequest request, KafkaReplaySnapshot? snapshot, IKafkaReplayProgressSink sink, CancellationToken token) => inner.ReplayTrackedAsync(request, snapshot, new FaultProgress(sink, state), token);
    }
    private sealed class FaultProgress(IKafkaReplayProgressSink inner, Faults state) : IKafkaReplayProgressSink
    {
        public Task BindSnapshotAsync(KafkaReplaySnapshot snapshot, CancellationToken token) => inner.BindSnapshotAsync(snapshot, token);
        public async Task BeforeMessageAsync(int partition, long offset, CancellationToken token)
        {
            await inner.BeforeMessageAsync(partition, offset, token);
            if (state.BlockBefore) { state.BlockBefore = false; state.Entered.TrySetResult(); await state.Release.Task.WaitAsync(token); }
        }
        public Task AfterMessageAsync(int partition, long offset, KafkaReplayConfirmedOutcome outcome, CancellationToken token)
        {
            if (state.LoseCheckpoint) { state.LoseCheckpoint = false; throw new IOException("模拟业务已提交而检查点不可用。"); }
            return inner.AfterMessageAsync(partition, offset, outcome, token);
        }
        public Task CompletePartitionAsync(int partition, long endOffsetExclusive, CancellationToken token) => inner.CompletePartitionAsync(partition, endOffsetExclusive, token);
    }
    private sealed class FaultAudit(ICommandExecutor command, IIdGenerator ids, IClock clock, Faults state) : ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>
    {
        private readonly MessagingDomainAuditWriter _inner = new(command, ids, clock);
        public Task WriteAsync(MessagingDomainAuditWrite item, CancellationToken token)
        {
            if (state.FailCompletionAudit && item.DiffSummaryJson is { } json)
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("state", out var status) && status.GetString() == "completed") throw new IOException("模拟终态审计失败。");
            }
            return _inner.WriteAsync(item, token);
        }
    }
    private sealed class LostCommit(ICommandTransaction inner, Faults state) : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token) => inner.ExecuteAsync(action, token);
        public async Task<Result<T>> ExecuteResultAsync<T>(Func<CancellationToken, Task<Result<T>>> action, CancellationToken token)
        {
            var value = await inner.ExecuteResultAsync(action, token);
            if (state.LoseFinalCommit && value.IsSuccess && value.Value is KafkaReplayOperationResponse { State: "completed" })
            { state.LoseFinalCommit = false; throw new IOException("模拟终态提交成功后响应丢失。"); }
            return value;
        }
    }
}
