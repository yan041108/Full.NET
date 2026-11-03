using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Approvals;
using Full.NET.Agents.Tools;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>仅测试夹具替换写处理器；正式审批、授权、事务 Outbox/Inbox 与审计保持真实。</summary>
[TestClass]
public sealed class AiControlledWriteReceiverTests
{
    private const string CommandType = "fixture.ai.controlled_write.command";
    private const string ReceiptType = "fixture.ai.controlled_write.receipt";
    private const string ToolName = "ai.chat.sessions.rename";

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Approved_write_outbox_inbox_and_receipt_survive_lost_responses_without_repeating_effects(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, cs, AiAgentRuntimeTestSettings.ForIntegrationTests(), configureTestServices: services =>
        {
            services.AddSingleton(IntegrationEventTopicDefinition.Create("fixture.ai.controlled-command.v1", CommandType, 1, EventDeliveryOwner.LegacyPolling));
            services.AddSingleton(IntegrationEventTopicDefinition.Create("fixture.ai.controlled-receipt.v1", ReceiptType, 1, EventDeliveryOwner.LegacyPolling));
        });
        await factory.InitializeAsync();
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(cs));
        await db.OpenAsync();
        var guid = provider == DatabaseProvider.SqlServer ? "uniqueidentifier" : "BINARY(16)";
        await db.ExecuteAsync($"CREATE TABLE demo_ai_fixture_sender (OperationId {guid} PRIMARY KEY, ActorId {guid} NOT NULL, SessionId {guid} NOT NULL, TargetId {guid} NOT NULL, ArgsHash varchar(64) NOT NULL, Phase varchar(32) NOT NULL, Version bigint NOT NULL)");
        await db.ExecuteAsync($"CREATE TABLE demo_ai_fixture_target (Id {guid} PRIMARY KEY, OwnerId {guid} NOT NULL, Title varchar(256) NOT NULL, Version bigint NOT NULL)");
        await db.ExecuteAsync($"CREATE TABLE demo_ai_fixture_effect (OperationId {guid} PRIMARY KEY, ArgsHash varchar(64) NOT NULL)");

        string[] permissions = [AiModelPermissions.Read, AiModelPermissions.Create, AiChatPermissions.Read, AiChatPermissions.Create,
            AiChatPermissions.Update, AiAgentRunPermissions.Read, AiAgentRunPermissions.Create, AiAgentApprovalPermissions.Read,
            AiAgentApprovalPermissions.Request, AiAgentToolPermissions.CatalogRead];
        var owner = await factory.CreateHostIdentityAsync("controlled-owner-" + Guid.NewGuid().ToString("N"), permissions);
        var approver = await factory.CreateHostIdentityAsync("controlled-approver-" + Guid.NewGuid().ToString("N"),
            [AiAgentApprovalPermissions.Read, AiAgentApprovalPermissions.Decide]);
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new("Bearer", owner.AccessToken);
        using var modelCreated = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "Controlled receiver", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.IsTrue(modelCreated.IsSuccessStatusCode, await modelCreated.Content.ReadAsStringAsync());
        var model = (await modelCreated.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        using var sessionCreated = await client.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(model.Id, "Original"));
        Assert.IsTrue(sessionCreated.IsSuccessStatusCode, await sessionCreated.Content.ReadAsStringAsync());
        var session = (await sessionCreated.Content.ReadFromJsonAsync<AiChatSessionResponse>())!;
        await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
        using var runCreated = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", new CreateAiAgentRunRequest(
            Guid.CreateVersion7(), "fullnet-single-text-v1", model.Id, "controlled fixture", 100, 100));
        Assert.AreEqual(HttpStatusCode.Accepted, runCreated.StatusCode, await runCreated.Content.ReadAsStringAsync());
        var run = (await runCreated.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!;
        await db.ExecuteAsync("INSERT INTO demo_ai_fixture_target (Id,OwnerId,Title,Version) VALUES (@Id,@Owner,'Original',1)", new { session.Id, Owner = owner.UserId });

        // 事件写入失败必须同时回滚审批消费和发送意图。
        var rollback = await ApprovedInvocation("Rollback");
        await db.ExecuteAsync($"ALTER TABLE fn_outbox_message ADD CONSTRAINT CK_fixture_controlled_outbox CHECK (MessageType <> '{CommandType}')");
        var rejected = await Execute(rollback.Invocation, false);
        Assert.AreEqual("ai.tool.reconciliation_required", rejected.ErrorCode);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_ai_fixture_sender WHERE OperationId=@Id", new { Id = rollback.Invocation.OperationId }));
        Assert.IsNull(await db.ExecuteScalarAsync<DateTimeOffset?>("SELECT ConsumedAtUtc FROM fn_ai_agent_approval WHERE Id=@Id", new { Id = rollback.ApprovalId }));
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_outbox_message DROP CONSTRAINT CK_fixture_controlled_outbox"
            : "ALTER TABLE fn_outbox_message DROP CHECK CK_fixture_controlled_outbox");

        // 提交后丢失发送响应，外层只能报告未知；可靠事实仍继续履约，不能重新执行已消费审批。
        var approved = await ApprovedInvocation("Approved title");
        var unknown = await Execute(approved.Invocation, true);
        Assert.AreEqual("ai.tool.reconciliation_required", unknown.ErrorCode);
        Assert.IsNotNull(await db.ExecuteScalarAsync<DateTimeOffset?>("SELECT ConsumedAtUtc FROM fn_ai_agent_approval WHERE Id=@Id", new { Id = approved.ApprovalId }));
        Assert.AreEqual("accepted", await Phase());
        Assert.AreEqual(1L, await TargetVersion());
        Assert.AreNotEqual("succeeded", (await Execute(approved.Invocation, false)).StatusKey);
        var command = await ReadEnvelope(CommandType);
        await Assert.ThrowsExactlyAsync<IOException>(() => ReceiveCommand(command, true));
        Assert.AreEqual(2L, await TargetVersion());
        await ReceiveCommand(command, false);
        await ReceiveCommand(Envelope(command.MessageType, command.Payload, Guid.CreateVersion7()), false);
        Assert.AreEqual(2L, await TargetVersion());
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_ai_fixture_effect"));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type", new { Type = ReceiptType }));

        var receipt = await ReadEnvelope(ReceiptType);
        await Assert.ThrowsExactlyAsync<IOException>(() => ReceiveReceipt(receipt, true));
        Assert.AreEqual("accepted", await Phase());
        await ReceiveReceipt(receipt, false); await ReceiveReceipt(receipt, false);
        await ReceiveReceipt(Envelope(receipt.MessageType, receipt.Payload, Guid.CreateVersion7()), false);
        Assert.AreEqual("completed", await Phase());
        Assert.AreEqual(2L, await db.ExecuteScalarAsync<long>("SELECT Version FROM demo_ai_fixture_sender WHERE OperationId=@Id", new { Id = approved.Invocation.OperationId }));
        var serializer = factory.Services.GetRequiredService<IIntegrationEventSerializer>();
        var tampered = serializer.Deserialize<string[]>(command.Payload); tampered[4] = "Unapproved";
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReceiveCommand(Envelope(command.MessageType, serializer.Serialize(tampered), command.EventId), false));
        using (var args = JsonDocument.Parse(JsonSerializer.Serialize(new { sessionId = session.Id, title = tampered[4] })))
            tampered[5] = AgentApprovalGate.ComputeArgumentsHash(args.RootElement);
        // 自洽的新输入也不能复用既有消息或操作身份；分别触发 Inbox 和业务操作去重约束。
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReceiveCommand(Envelope(command.MessageType, serializer.Serialize(tampered), command.EventId), false));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReceiveCommand(Envelope(command.MessageType, serializer.Serialize(tampered), Guid.CreateVersion7()), false));
        var foreignReceipt = serializer.Deserialize<string[]>(receipt.Payload); foreignReceipt[1] = Guid.CreateVersion7().ToString("D");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReceiveReceipt(Envelope(receipt.MessageType, serializer.Serialize(foreignReceipt), Guid.CreateVersion7()), false));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReceiveReceipt(Envelope(receipt.MessageType, receipt.Payload, receipt.EventId, Guid.CreateVersion7()), false));
        Assert.AreEqual(2L, await TargetVersion());
        Assert.AreEqual("ai.tool.reconciliation_required", await db.ExecuteScalarAsync<string>("SELECT ErrorCode FROM fn_ai_agent_tool_call WHERE Id=@Id AND RunId=@Run AND ApprovalId=@Approval",
            new { Id = approved.Invocation.OperationId, Run = run.RunId, Approval = approved.ApprovalId }));

        async Task<(ToolInvocation Invocation, Guid ApprovalId)> ApprovedInvocation(string title)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", owner.AccessToken);
            var operation = Guid.CreateVersion7();
            var text = JsonSerializer.Serialize(new { sessionId = session.Id, title });
            using var created = await client.PostAsJsonAsync("/api/v1/ai/agent/approvals", new CreateAiAgentApprovalRequest(run.RunId, operation, ToolName, 1, text));
            Assert.IsTrue(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
            var approval = (await created.Content.ReadFromJsonAsync<CreateAiAgentApprovalResponse>())!;
            var pending = (await client.GetFromJsonAsync<AiAgentApprovalResponse>($"/api/v1/ai/agent/approvals/{approval.ApprovalId}"))!;
            client.DefaultRequestHeaders.Authorization = new("Bearer", approver.AccessToken);
            using var decided = await client.PostAsJsonAsync($"/api/v1/ai/agent/approvals/{approval.ApprovalId}/decide", new DecideAiAgentApprovalRequest(true, pending.Version));
            Assert.IsTrue(decided.IsSuccessStatusCode, await decided.Content.ReadAsStringAsync());
            using var args = JsonDocument.Parse(text);
            return (new(operation, run.RunId, ToolName, 1, args.RootElement.Clone()), approval.ApprovalId);
        }
        async Task<ToolExecutionResult> Execute(ToolInvocation invocation, bool loseResponse)
        {
            await using var scope = factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var context = sp.GetRequiredService<ICurrentTenantContextWriter>(); context.SetHost();
            var http = sp.GetRequiredService<IHttpContextAccessor>(); var previous = http.HttpContext;
            http.HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity(new JsonWebToken(owner.AccessToken).Claims, "server-issued-fixture")) };
            try
            {
                var handler = new Sender(sp.GetRequiredService<AiAgentApprovalConsumption>(), sp.GetRequiredService<ICommandTransaction>(),
                    sp.GetRequiredService<ICommandExecutor>(), sp.GetRequiredService<IOutboxWriter>(), loseResponse);
                var executor = new AgentToolExecutor(new Registry(handler), sp.GetRequiredService<IToolAuthorizationPort>(),
                    sp.GetRequiredService<IToolAuditPort>(), sp.GetRequiredService<IAgentApprovalPort>());
                return await executor.ExecuteAsync(invocation);
            }
            finally { context.Clear(); http.HttpContext = previous; }
        }
        async Task<IntegrationEventEnvelope> ReadEnvelope(string type)
        {
            var row = await db.QuerySingleAsync<EventRow>("SELECT Id,Payload FROM fn_outbox_message WHERE MessageType=@Type", new { Type = type });
            return Envelope(type, row.Payload, row.Id);
        }
        Task<long> TargetVersion() => db.ExecuteScalarAsync<long>("SELECT Version FROM demo_ai_fixture_target WHERE Id=@Id", new { session.Id });
        Task<string?> Phase() => db.ExecuteScalarAsync<string>("SELECT Phase FROM demo_ai_fixture_sender WHERE OperationId=@Id", new { Id = approved.Invocation.OperationId });

        async Task ReceiveCommand(IntegrationEventEnvelope envelope, bool loseResponse)
        {
            await InReceiverScope(async (sp, ct) =>
            {
                var payload = Read(envelope, CommandType, sp);
                var inbox = sp.GetRequiredService<IIntegrationEventInbox>();
                var claim = await inbox.ClaimAsync("fullnet.ai.fixture.receiver", envelope, ct);
                if (claim.Status == InboxClaimStatus.PayloadMismatch) throw new InvalidDataException("Payload changed.");
                if (claim.Status == InboxClaimStatus.AlreadyProcessed) return;
                var q = sp.GetRequiredService<IQueryExecutor>(); var c = sp.GetRequiredService<ICommandExecutor>();
                var p = Parameters(payload);
                var existing = await q.QuerySingleOrDefaultAsync<string>(S("effect", "SELECT ArgsHash FROM demo_ai_fixture_effect WHERE OperationId=@OperationId"), p, ct);
                if (existing is null)
                {
                    if (await c.ExecuteAsync(S("change", "UPDATE demo_ai_fixture_target SET Title=@Title,Version=Version+1 WHERE Id=@TargetId AND OwnerId=@ActorId AND Version=1"), p, ct) != 1)
                        throw new InvalidDataException("Target ownership or version changed.");
                    await c.ExecuteAsync(S("effect_insert", "INSERT INTO demo_ai_fixture_effect (OperationId,ArgsHash) VALUES (@OperationId,@ArgsHash)"), p, ct);
                    await sp.GetRequiredService<IOutboxWriter>().AddAsync(ReceiptType, 1, payload, ct);
                }
                else if (existing != payload[5]) throw new InvalidDataException("Operation identity changed.");
                await inbox.MarkProcessedAsync("fullnet.ai.fixture.receiver", envelope.EventId, ct);
            });
            if (loseResponse) throw new IOException("Receiver committed, acknowledgement lost.");
        }
        Task ReceiveReceipt(IntegrationEventEnvelope envelope, bool failCommit) => InReceiverScope(async (sp, ct) =>
        {
            var payload = Read(envelope, ReceiptType, sp);
            var inbox = sp.GetRequiredService<IIntegrationEventInbox>();
            var claim = await inbox.ClaimAsync("fullnet.ai.fixture.sender", envelope, ct);
            if (claim.Status == InboxClaimStatus.PayloadMismatch) throw new InvalidDataException("Receipt changed.");
            if (claim.Status == InboxClaimStatus.AlreadyProcessed) return;
            var p = Parameters(payload); var q = sp.GetRequiredService<IQueryExecutor>();
            var phase = await q.QuerySingleOrDefaultAsync<string>(S("intent_match", "SELECT Phase FROM demo_ai_fixture_sender WHERE OperationId=@OperationId AND ActorId=@ActorId AND SessionId=@SessionId AND TargetId=@TargetId AND ArgsHash=@ArgsHash"), p, ct);
            if (phase is null) throw new InvalidDataException("Receipt does not match the approved intent.");
            if (phase == "accepted") await sp.GetRequiredService<ICommandExecutor>().ExecuteAsync(S("receipt", "UPDATE demo_ai_fixture_sender SET Phase='completed',Version=Version+1 WHERE OperationId=@OperationId AND Phase='accepted'"), p, ct);
            await inbox.MarkProcessedAsync("fullnet.ai.fixture.sender", envelope.EventId, ct);
            if (failCommit) throw new IOException("Receipt transaction failed.");
        });
        async Task InReceiverScope(Func<IServiceProvider, CancellationToken, Task> action)
        {
            await using var scope = factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var context = sp.GetRequiredService<ICurrentTenantContextWriter>(); context.SetHost();
            try { await sp.GetRequiredService<ICommandTransaction>().ExecuteAsync(async ct => { await action(sp, ct); return true; }, default); }
            finally { context.Clear(); }
        }
    }

    // 夹具使用固定长度的具体 string[] MemoryPack 协议；六个字段不接受多态、额外主体或任意命令。
    private static string[] Read(IntegrationEventEnvelope envelope, string type, IServiceProvider sp)
    {
        if (envelope.MessageType != type || envelope.SchemaVersion != 1 || envelope.TenantId is not null) throw new InvalidDataException("Wrong envelope scope.");
        var values = sp.GetRequiredService<IIntegrationEventSerializer>().Deserialize<string[]>(envelope.Payload);
        if (values.Length != 6 || values.Any(string.IsNullOrWhiteSpace) || values[4].Length > 256) throw new InvalidDataException("Invalid fixed command.");
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { sessionId = Guid.Parse(values[3]), title = values[4] }));
        if (AgentApprovalGate.ComputeArgumentsHash(args.RootElement) != values[5]) throw new InvalidDataException("Approved arguments changed.");
        return values;
    }
    private static Dictionary<string, object?> Parameters(string[] values) => new()
    { ["OperationId"] = Guid.Parse(values[0]), ["ActorId"] = Guid.Parse(values[1]), ["SessionId"] = Guid.Parse(values[2]),
        ["TargetId"] = Guid.Parse(values[3]), ["Title"] = values[4], ["ArgsHash"] = values[5] };
    private static SqlStatement S(string name, string sql) => new("fixture.ai.controlled." + name, sql, SqlDataScope.HostOnly);
    private static IntegrationEventEnvelope Envelope(string type, ReadOnlyMemory<byte> payload, Guid id, Guid? tenant = null) =>
        IntegrationEventEnvelope.Create(id, type, 1, MessagingNames.ContentTypeMemoryPack, tenant, "controlled-write", null,
            null, null, "fullnet.fixture.ai", DateTimeOffset.UtcNow, payload);
    private sealed record EventRow(Guid Id, byte[] Payload);
    private sealed class Registry(IAgentToolHandler handler) : IAgentToolRegistrySource
    {
        public ValueTask<AgentToolRegistry> GetRegistryAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AgentToolRegistry([new(ToolName, 1, AiChatPermissions.Update, "write", true, handler)]));
    }
    private sealed class Sender(AiAgentApprovalConsumption approvals, ICommandTransaction transaction, ICommandExecutor commands,
        IOutboxWriter outbox, bool loseResponse) : IAgentToolHandler
    {
        public bool ValidateArguments(JsonElement arguments) => arguments.ValueKind == JsonValueKind.Object
            && arguments.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "sessionId", "title" })
            && arguments.GetProperty("sessionId").TryGetGuid(out var id) && id != Guid.Empty && arguments.GetProperty("title").GetString() is { Length: > 0 and <= 256 };
        public async ValueTask<JsonElement> ExecuteAsync(ToolInvocation invocation, ToolActor actor, CancellationToken cancellationToken)
        {
            if (await approvals.ValidateForToolExecutionAsync(invocation, actor, cancellationToken) != AgentApprovalExecutionStatus.Approved)
                throw new InvalidOperationException("Approval unavailable.");
            string[] payload = [invocation.OperationId.ToString("D"), actor.UserId.ToString("D"), actor.SessionId.ToString("D"),
                invocation.Arguments.GetProperty("sessionId").GetGuid().ToString("D"), invocation.Arguments.GetProperty("title").GetString()!, AgentApprovalGate.ComputeArgumentsHash(invocation.Arguments)];
            await transaction.ExecuteAsync(async ct =>
            {
                if (!await approvals.TryConsumeForToolExecutionAsync(invocation, actor, ct)) throw new InvalidOperationException("Approval not consumed.");
                await commands.ExecuteAsync(S("send", "INSERT INTO demo_ai_fixture_sender (OperationId,ActorId,SessionId,TargetId,ArgsHash,Phase,Version) VALUES (@OperationId,@ActorId,@SessionId,@TargetId,@ArgsHash,'accepted',1)"), Parameters(payload), ct);
                await outbox.AddAsync(CommandType, 1, payload, ct);
                return true;
            }, cancellationToken);
            if (loseResponse) throw new IOException("Sender committed, response lost.");
            return JsonSerializer.SerializeToElement(new { status = "accepted", operationId = invocation.OperationId });
        }
    }
}
