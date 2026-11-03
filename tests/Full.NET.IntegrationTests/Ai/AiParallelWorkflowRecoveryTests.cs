using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Full.NET.Agents.Workflows;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Ai.Features.ManageAgentTools;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库、框架适配器和正式授权/审批/预算，模型网络仅使用明确计量的受控提供者。</summary>
[TestClass]
public sealed class AiParallelWorkflowRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)] [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, false)] [DataRow(DatabaseProvider.MySql, true)]
    public async Task Target_binding_parallel_join_and_original_approval_survive_partial_wave_handoff(DatabaseProvider provider, bool handoff)
    {
        await using var f = await Fixture.Create(provider);
        var request = new CreateAiAgentRunRequest(Guid.CreateVersion7(), AgentWorkflowRegistry.ChatReviewWorkflowKey,
            f.Model, "", 100, 100, TargetChatSessionId: f.Target);
        using (var missing = await f.Client.PostAsJsonAsync("/api/v1/ai/agent/runs", request with { TargetChatSessionId = null }))
            Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var foreign = await f.Client.PostAsJsonAsync("/api/v1/ai/agent/runs", request with { TargetChatSessionId = Guid.CreateVersion7() }))
            Assert.AreEqual(HttpStatusCode.Forbidden, foreign.StatusCode);
        var run = await f.CreateRun(request);
        using (var replay = await f.Client.PostAsJsonAsync("/api/v1/ai/agent/runs", request))
            Assert.AreEqual(run, (await replay.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!.RunId);
        using (var changed = await f.Client.PostAsJsonAsync("/api/v1/ai/agent/runs", request with { TargetChatSessionId = Guid.CreateVersion7() }))
            Assert.AreEqual(HttpStatusCode.Conflict, changed.StatusCode);

        if (handoff) await f.SaveFirstWave(run);
        await f.Process();
        Assert.AreEqual("awaiting_approval", await f.Status(run));
        Assert.AreEqual(2, f.Models.MaximumActive);
        Assert.AreEqual(6, f.Models.Prompts.Count);
        Assert.AreEqual(4, f.Models.Prompts.Count(prompt => prompt.Contains("reviewer:", StringComparison.Ordinal)));
        Assert.AreEqual("Original", await f.Title());
        using var proposalResponse = await f.Client.GetAsync($"/api/v1/ai/agent/runs/{run}/workflow-proposal");
        Assert.AreEqual(HttpStatusCode.OK, proposalResponse.StatusCode, await proposalResponse.Content.ReadAsStringAsync());
        var proposal = (await proposalResponse.Content.ReadFromJsonAsync<AiAgentWorkflowProposalResponse>())!;
        Assert.AreEqual(f.Target, proposal.TargetChatSessionId);
        Assert.AreNotEqual(f.Binding.SessionId, proposal.TargetChatSessionId);
        Assert.AreEqual("Reviewed title", proposal.ProposedTitle);
        using var args = JsonDocument.Parse(proposal.ArgumentsJson);
        Assert.AreEqual(f.Target, args.RootElement.GetProperty("sessionId").GetGuid());
        Assert.AreEqual(2, args.RootElement.EnumerateObject().Count());
        using var approvalCreated = await f.Client.PostAsJsonAsync("/api/v1/ai/agent/approvals", new CreateAiAgentApprovalRequest(
            run, proposal.OperationId, proposal.ToolName, proposal.ToolVersion, proposal.ArgumentsJson));
        Assert.IsTrue(approvalCreated.IsSuccessStatusCode, await approvalCreated.Content.ReadAsStringAsync());
        var approvalId = (await approvalCreated.Content.ReadFromJsonAsync<CreateAiAgentApprovalResponse>())!.ApprovalId;
        var pending = (await f.Client.GetFromJsonAsync<AiAgentApprovalResponse>($"/api/v1/ai/agent/approvals/{approvalId}"))!;
        Assert.AreEqual("Original", pending.TargetSummary);
        f.Client.DefaultRequestHeaders.Authorization = new("Bearer", f.ApproverToken);
        using (var otherProposal = await f.Client.GetAsync($"/api/v1/ai/agent/runs/{run}/workflow-proposal"))
            Assert.IsTrue(otherProposal.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        using var approved = await f.Client.PostAsJsonAsync($"/api/v1/ai/agent/approvals/{approvalId}/decide", new DecideAiAgentApprovalRequest(true, pending.Version));
        Assert.IsTrue(approved.IsSuccessStatusCode, await approved.Content.ReadAsStringAsync());
        f.Client.DefaultRequestHeaders.Authorization = new("Bearer", f.OwnerToken);
        using var resumed = await f.Client.PostAsync($"/api/v1/ai/agent/runs/{run}/resume", null);
        Assert.IsTrue(resumed.IsSuccessStatusCode, await resumed.Content.ReadAsStringAsync());
        await f.Process();
        Assert.AreEqual("completed", await f.Status(run));
        Assert.AreEqual("Reviewed title", await f.Title());
        Assert.AreEqual(6, f.Models.Prompts.Count, "批准后恢复不能重新调用已完成模型。");
        Assert.AreEqual(2, await f.Db.ExecuteScalarAsync<int>("SELECT Version FROM fn_ai_chat_session WHERE Id=@Id", new { Id = f.Target }));
        Assert.AreEqual(1, await f.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_agent_approval WHERE Id=@Id AND ConsumedAtUtc IS NOT NULL", new { Id = approvalId }));
        var usage = await f.Db.QuerySingleAsync<Usage>("SELECT InputTokens,OutputTokens,ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = run });
        Assert.AreEqual(12L, usage.InputTokens); Assert.AreEqual(6L, usage.OutputTokens); Assert.AreEqual(18L, usage.ChargedTokens);
        await f.Process();
        Assert.AreEqual(2, await f.Db.ExecuteScalarAsync<int>("SELECT Version FROM fn_ai_chat_session WHERE Id=@Id", new { Id = f.Target }));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Session_revocation_during_parallel_dispatch_stops_new_roles_and_never_writes(DatabaseProvider provider)
    {
        await using var f = await Fixture.Create(provider);
        var run = await f.CreateRun(new(Guid.CreateVersion7(), AgentWorkflowRegistry.ChatReviewWorkflowKey, f.Model, "", 100, 100, TargetChatSessionId: f.Target));
        f.Models.FirstRole = () => f.Db.ExecuteAsync("UPDATE fn_identity_refresh_session SET RevokedAtUtc=@Now,Version=Version+1 WHERE Id=@Id", new { Id = f.Binding.SessionId, Now = DateTimeOffset.UtcNow });
        await f.Process();
        Assert.AreNotEqual("completed", await f.Status(run));
        Assert.AreEqual("Original", await f.Title());
        Assert.IsTrue(f.Models.Prompts.Count <= 2);
        Assert.IsFalse(f.Models.Prompts.Any(prompt => prompt.StartsWith("Propose", StringComparison.Ordinal)));
        Assert.AreEqual(0, f.Models.Active);
        Assert.AreEqual(0, await f.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_agent_approval WHERE RunId=@Id", new { Id = run }));
        Assert.AreEqual(200L, await f.Db.ExecuteScalarAsync<long>("SELECT ReservedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = run }));
    }

    private sealed record Usage(long? InputTokens, long? OutputTokens, long ChargedTokens);

    private sealed class Fixture : IAsyncDisposable
    {
        internal required FullNetApiFactory Factory;
        internal required HttpClient Client;
        internal required DbConnection Db;
        internal required Models Models;
        internal required string OwnerToken, ApproverToken;
        internal required SessionBindingSnapshot Binding;
        internal Guid Model, Target;

        internal static async Task<Fixture> Create(DatabaseProvider provider)
        {
            var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
            var models = new Models();
            var factory = new FullNetApiFactory(provider, cs, AiAgentRuntimeTestSettings.ForIntegrationTests(), configureTestServices: services =>
            {
                services.RemoveAll<IAiModelClientFactory>(); services.AddSingleton<IAiModelClientFactory>(models);
                services.AddScoped<IAgentRunExecutionContext, AgentRunExecutionContext>();
                services.AddScoped<AiToolAuthorizationPort>(); services.AddScoped<AiBackgroundToolAuthorizationPort>();
                services.AddScoped<AiToolAuditPort>(); services.AddScoped<AiBackgroundToolAuditPort>();
                services.Replace(ServiceDescriptor.Scoped<IToolAuthorizationPort>(sp => new MixedAuthorization(sp.GetRequiredService<IAgentRunExecutionContext>(), sp.GetRequiredService<AiToolAuthorizationPort>(), sp.GetRequiredService<AiBackgroundToolAuthorizationPort>())));
                services.Replace(ServiceDescriptor.Scoped<IToolAuditPort>(sp => new MixedAudit(sp.GetRequiredService<IAgentRunExecutionContext>(), sp.GetRequiredService<AiToolAuditPort>(), sp.GetRequiredService<AiBackgroundToolAuditPort>())));
            });
            await factory.InitializeAsync();
            string[] permissions = [AiModelPermissions.Read, AiModelPermissions.Create, AiChatPermissions.Read, AiChatPermissions.Create, AiChatPermissions.Update,
                AiAgentRunPermissions.Read, AiAgentRunPermissions.Create, AiAgentRunPermissions.Resume, AiAgentApprovalPermissions.Read, AiAgentApprovalPermissions.Request, AiAgentToolPermissions.CatalogRead];
            var owner = await factory.CreateHostIdentityAsync("parallel-owner-" + Guid.NewGuid().ToString("N"), permissions);
            var approver = await factory.CreateHostIdentityAsync("parallel-approver-" + Guid.NewGuid().ToString("N"), [AiAgentRunPermissions.Read, AiAgentApprovalPermissions.Read, AiAgentApprovalPermissions.Decide]);
            var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
            using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(null, "Parallel model", "ollama", "https://provider.test", "model", null, null, false, true));
            Assert.IsTrue(modelResponse.IsSuccessStatusCode, await modelResponse.Content.ReadAsStringAsync());
            var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
            using var targetResponse = await client.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(model.Id, "Original"));
            Assert.IsTrue(targetResponse.IsSuccessStatusCode, await targetResponse.Content.ReadAsStringAsync());
            var target = (await targetResponse.Content.ReadFromJsonAsync<AiChatSessionResponse>())!;
            var context = new DefaultHttpContext { User = new(new ClaimsIdentity(new JsonWebToken(owner.AccessToken).Claims, "server-issued-fixture")) };
            Assert.IsTrue(AgentRunHttpBinding.TryCreate(context, null, out var binding));
            await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
            DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(cs));
            await db.OpenAsync();
            return new() { Factory = factory, Client = client, Db = db, Models = models, OwnerToken = owner.AccessToken, ApproverToken = approver.AccessToken, Binding = binding, Model = model.Id, Target = target.Id };
        }
        internal async Task<Guid> CreateRun(CreateAiAgentRunRequest request)
        {
            using var response = await Client.PostAsJsonAsync("/api/v1/ai/agent/runs", request);
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!.RunId;
        }
        internal async Task Process()
        {
            await using var scope = Factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var tenant = sp.GetRequiredService<ICurrentTenantContextWriter>(); tenant.SetHost();
            try
            {
                var coordinator = new AiAgentRunCoordinator(Factory.Services.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<AiAgentRunStore>(),
                    sp.GetRequiredService<AiAgentWorkerHeartbeatService>(), AgentFrameworkRuntime.CreateRunner(), sp.GetRequiredService<IClock>(), sp.GetRequiredService<IIdGenerator>(),
                    Options.Create(new AiAgentRuntimeOptions { AcceptsNewRuns = true, RuntimeVersion = "1", PollMilliseconds = 60000 }), NullLogger<AiAgentRunCoordinator>.Instance);
                await coordinator.ProcessPendingAsync();
            }
            finally { tenant.Clear(); }
        }
        internal async Task SaveFirstWave(Guid runId)
        {
            await using var scope = Factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var tenant = sp.GetRequiredService<ICurrentTenantContextWriter>(); tenant.SetHost();
            var execution = sp.GetRequiredService<IAgentRunExecutionContext>(); execution.SetBinding(Binding);
            try
            {
                var store = sp.GetRequiredService<AiAgentRunStore>();
                var lease = (await store.TryAcquireAsync(runId, "first-wave-owner", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)))!;
                var definition = AgentWorkflowRegistry.Resolve(AgentWorkflowRegistry.ChatReviewWorkflowKey, 1)!;
                var state = AgentWorkflowState.Create(Binding.SessionId); state.TargetChatSessionId = Target;
                state.Binding = new(runId, definition.Key, definition.Version, AgentWorkflowNodeReceipt.DigestDefinition(definition), Model, 1);
                var registry = await sp.GetRequiredService<IAgentToolRegistrySource>().GetRegistryAsync();
                var sequence = await sp.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<long>(AiAgentRunSql.FindLatestEventSequence, AiSqlParameters.Create(("RunId", runId)), default);
                using var raw = Models.NewClient();
                using var admission = AiChatInputBudgetScope.Begin(100, async token =>
                {
                    await using var checkScope = Factory.Services.CreateAsyncScope(); var checks = checkScope.ServiceProvider;
                    var writer = checks.GetRequiredService<ICurrentTenantContextWriter>(); writer.SetHost();
                    try { if (!await checks.GetRequiredService<AiWorkflowTargetAdmission>().AuthorizeAsync(Target, Binding, token)) throw new UnauthorizedAccessException(); }
                    finally { writer.Clear(); }
                });
                var result = await new AgentWorkflowRunner().RunAsync(new(runId, definition.Key, definition.Version, state, raw,
                    AgentFrameworkRuntime.CreateRunner(), sp.GetRequiredService<IAgentToolExecutor>(), registry, key => AgentWorkflowNodeReceipt.ResolveOperationId(runId, key))
                {
                    InputTokenLimit = 100, OutputTokenLimit = 100,
                    ModelClientFactory = allocation => new AiRunOutputLimitClient(raw, allocation.OutputTokenLimit, inputTokenLimit: allocation.InputTokenLimit),
                    PersistNodeAsync = async (receipt, saved, token) =>
                    {
                        var payload = saved.ToJson(); sequence++;
                        var step = receipt.OutputDigest is null ? null : new AgentStepUpsert(Guid.CreateVersion7(), receipt.NodeKey, 1, receipt.OperationId, "committed",
                            receipt.ToolVersion is null ? 1 : null, receipt.ToolVersion, null, receipt.InputTokens, receipt.OutputTokens, null, null, null, receipt.InputDigest, null);
                        Assert.IsTrue(await store.CommitProgressAsync(new(lease, "running", step,
                            new(Guid.CreateVersion7(), sequence, AgentCheckpointCompatibility.CurrentCheckpointFormatVersion, AgentFrameworkRuntime.FrameworkVersion, 1, payload,
                                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))),
                            new(Guid.CreateVersion7(), sequence, receipt.OutputDigest is null ? "run.node_started" : "run.node_completed", 1, "{}")), token));
                        lease = lease with { Version = lease.Version + 1 };
                        return saved.NextNodeIndex != 3 || saved.ParallelPendingNodes.Count != 0;
                    }
                });
                Assert.AreEqual(AgentWorkflowRunStatus.ReconciliationRequired, result.Status);
                Assert.AreEqual(2, Models.Prompts.Count);
                var checkpoint = await store.FindLatestCheckpointAsync(runId);
                Assert.AreEqual(3, AiWorkflowCheckpointAdmission.Restore((await store.FindByIdAsync(runId))!, checkpoint)!.NextNodeIndex);
            }
            finally { execution.Clear(); tenant.Clear(); }
            // 模拟原执行者停止后的到期租约；新协调器必须按持久凭证恢复，不能重跑第一波。
            await Db.ExecuteAsync("UPDATE fn_ai_agent_run SET LeaseExpiresAtUtc=@Expired,Version=Version+1 WHERE Id=@Id", new { Id = runId, Expired = DateTimeOffset.UtcNow.AddMinutes(-1) });
        }
        internal Task<string?> Status(Guid id) => Db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_ai_agent_run WHERE Id=@Id", new { Id = id });
        internal Task<string?> Title() => Db.ExecuteScalarAsync<string>("SELECT Title FROM fn_ai_chat_session WHERE Id=@Id", new { Id = Target });
        public async ValueTask DisposeAsync() { Client.Dispose(); await Db.DisposeAsync(); await Factory.DisposeAsync(); }
    }

    private sealed class Models : IAiModelClientFactory
    {
        public string ProviderKey => "ollama";
        internal readonly ConcurrentQueue<string> Prompts = new();
        internal int Active, MaximumActive, InvokedFirst;
        internal Func<Task>? FirstRole;
        internal IChatClient NewClient() => new Client(this);
        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken) => ValueTask.FromResult(NewClient());
        public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken cancellationToken) => throw new NotSupportedException();
        private sealed class Client(Models owner) : IChatClient
        {
            public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                await AiChatInputBudgetScope.RequireCurrent().RevalidateAsync(cancellationToken);
                var prompt = string.Join("\n", messages.Select(message => message.Text));
                owner.Prompts.Enqueue(prompt); var role = prompt.Contains("reviewer:", StringComparison.Ordinal);
                var active = Interlocked.Increment(ref owner.Active);
                owner.MaximumActive = Math.Max(owner.MaximumActive, active);
                try
                {
                    if (role)
                    {
                        if (Interlocked.Exchange(ref owner.InvokedFirst, 1) == 0 && owner.FirstRole is { } revoke) await revoke();
                        await Task.Delay(30, cancellationToken);
                    }
                    var text = role ? "Review" : prompt.StartsWith("Propose", StringComparison.Ordinal) ? "Reviewed title" : """{"decision":"approved"}""";
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { Usage = new UsageDetails { InputTokenCount = 2, OutputTokenCount = 1 } };
                }
                finally { Interlocked.Decrement(ref owner.Active); }
            }
            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == typeof(IChatClient) && serviceKey is null ? this : null;
            public void Dispose() { }
        }
    }
    // 测试宿主同时承载 API 与显式调用的 Worker，逐次委托给正式适配器，不伪造授权或审计。
    private sealed class MixedAuthorization(IAgentRunExecutionContext context, AiToolAuthorizationPort api, AiBackgroundToolAuthorizationPort worker) : IToolAuthorizationPort
    { public ValueTask<ToolActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken) => context.Binding is null ? api.AuthorizeAsync(permissionCode, cancellationToken) : worker.AuthorizeAsync(permissionCode, cancellationToken); }
    private sealed class MixedAudit(IAgentRunExecutionContext context, AiToolAuditPort api, AiBackgroundToolAuditPort worker) : IToolAuditPort
    {
        public ValueTask BeginAsync(ToolInvocation invocation, string permissionCode, string statusKey, string? errorCode, CancellationToken cancellationToken) => context.Binding is null ? api.BeginAsync(invocation, permissionCode, statusKey, errorCode, cancellationToken) : worker.BeginAsync(invocation, permissionCode, statusKey, errorCode, cancellationToken);
        public ValueTask CompleteAsync(Guid operationId, string statusKey, string? errorCode, int outputBytes, int durationMs, CancellationToken cancellationToken) => context.Binding is null ? api.CompleteAsync(operationId, statusKey, errorCode, outputBytes, durationMs, cancellationToken) : worker.CompleteAsync(operationId, statusKey, errorCode, outputBytes, durationMs, cancellationToken);
    }
}
