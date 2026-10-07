using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Microsoft.Extensions.AI;
using NSubstitute;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using System.Net;
using System.Text;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod]
    public async Task Paid_answer_freezes_entry_scope_before_initial_shared_model_query_wait()
    {
        using var f = await PaidAnswerFixture.CreateAsync(true);
        f.R.Query.BeforeGenerationModel = _ => { f.R.F.Tenant.SetTenant(new(Guid.CreateVersion7(), "new", "另一租户")); return Task.CompletedTask; };
        await Assert.ThrowsAsync<Exception>(() => f.ExecuteAsync());
        Assert.AreEqual(0, f.R.Query.SourceReads); Assert.AreEqual(0, f.Budget.Requests.Count);
        Assert.AreEqual(0, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
    }
    [TestMethod]
    public async Task Paid_answer_executes_real_openai_factory_with_fixed_http_output_bound()
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var previous = f.R.Model;
        f.R.Model = new() { Id = previous.Id, Version = previous.Version, TenantId = previous.TenantId,
            ProviderKey = previous.ProviderKey, ModelId = previous.ModelId, EndpointBaseUrl = previous.EndpointBaseUrl,
            IsEnabled = true, ApiKeyProtected = TestAiProviders.Protect("fixture-key") }; f.R.Query.Model = f.R.Model;
        f.Generation = PaidAnswerFixture.Model(f.Generation.Id, credential: TestAiProviders.Protect("fixture-key")); f.R.Query.GenerationModel = f.Generation;
        using var handler = new PaidAnswerHttpHandler(); var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        f.UseFactories(TestAiProviders.Create(httpFactory, f.bindings));
        var result = await f.ExecuteAsync(); Assert.AreEqual("真实客户端答案", result.Answer.Text); Assert.AreEqual(2, handler.Calls);
        Assert.AreEqual(7, f.Budget.Settlements[^1].Usage.InputTokens); Assert.AreEqual(3, f.Budget.Settlements[^1].Usage.OutputTokens);
    }
    [TestMethod]
    public async Task Paid_answer_checks_send_withdrawal_inside_last_read_permission_chain()
    {
        using var f = await PaidAnswerFixture.CreateAsync(false);
        f.Factory.BeforeCreate = kind => { if (kind == "chat") f.R.BeforePermission = permission =>
            { if (permission == AiKnowledgeDocumentPermissions.IndexRead) f.R.DeniedPermission = AiChatPermissions.Send; }; };
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => f.ExecuteAsync());
        Assert.AreEqual(0, f.Factory.ChatCalls); Assert.AreEqual(new AiOperationUsage(0, 0), f.Budget.Settlements[^1].Usage);
    }
    [TestMethod] [DataRow("create")] [DataRow("generate")] [DataRow("dispose")]
    public async Task Paid_answer_noncooperative_model_cannot_hold_request_or_deliver_late_body(string stage)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Factory.BlockStage = stage; f.Factory.BlockEntered = entered; f.Factory.BlockRelease = release.Task;
        var pending = f.ExecuteAsync(token: stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); stop.Cancel();
        try
        {
            var done = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(7)));
            Assert.AreSame(pending, done, "取消与清理期限后仍由非合作模型占住请求。");
            await Assert.ThrowsExactlyAsync<IOException>(() => pending);
            Assert.AreEqual(2, f.Budget.Settlements.Count);
            Assert.AreEqual(stage == "create" ? 0 : stage == "generate" ? null : 7, f.Budget.Settlements[^1].Usage.InputTokens);
        }
        finally { release.TrySetResult(); try { await pending; } catch { } }
        await f.Factory.LateDisposed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(stage == "create" ? 0 : 1, f.Factory.ChatCalls);
        Assert.AreEqual(2, f.Budget.Settlements.Count);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)] [DataRow(false, true)] [DataRow(true, true)]
    public async Task Paid_answer_runs_two_budgeted_stages_and_retains_all_dependencies(bool tenant, bool unknown = false)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); if (unknown) f.Factory.Usage = null;
        var result = await f.ExecuteAsync();
        Assert.AreEqual("有依据的答案", result.Answer.Text); Assert.AreEqual(1, result.Answer.Citations.Count);
        Assert.AreEqual(17, result.Prepared.Retrieval.Chunks.Count); Assert.AreEqual(1, result.Prepared.Retrieval.Sources.Count);
        Assert.AreEqual(2, f.Budget.Requests.Count); Assert.AreEqual(2, f.Budget.Settlements.Count);
        Assert.AreEqual("embedding", f.Budget.Requests[0].Kind); Assert.AreEqual("chat", f.Budget.Requests[1].Kind);
        Assert.AreNotEqual(result.EmbeddingOperationId, result.GenerationOperationId);
        Assert.IsTrue(f.Budget.Settlements.All(s => s.Outcome == "succeeded"));
        Assert.IsTrue(f.Budget.Settlements.All(s => s.Usage.InputTokens == (unknown ? null : 7)));
        Assert.AreEqual(unknown ? null : 3, f.Budget.Settlements[1].Usage.OutputTokens);
        Assert.IsTrue(f.Factory.EmbeddingDisposed && f.Factory.ChatDisposed);
        Assert.AreEqual(0, f.Factory.StreamCalls);
        Assert.IsNull(f.Factory.Options);
        Assert.AreEqual("system", f.Factory.Messages![0].Role.Value);
        Assert.AreEqual("user", f.Factory.Messages[1].Role.Value);
        Assert.AreEqual("问题", JsonDocument.Parse(f.Factory.Messages[1].Text).RootElement.GetProperty("question").GetString());
    }
    [TestMethod] [DataRow("permission")] [DataRow("source")] [DataRow("model")]
    [DataRow("transaction")] [DataRow("question")] [DataRow("ids")] [DataRow("payload")]
    public async Task Paid_answer_refuses_invalid_requests_before_any_charge(string fault)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false);
        if (fault == "permission") f.R.Denied = true;
        if (fault == "source") f.R.Query.Rows = [];
        if (fault == "payload") f.R.F.Payloads[1].Payload[0] ^= 1;
        if (fault == "model") f.R.Query.GenerationModel = PaidAnswerFixture.Model(f.Generation.Id, "https://changed.example/v1");
        if (fault == "transaction") f.R.F.Transactions.HasTransaction.Returns(true);
        await Assert.ThrowsAsync<Exception>(() => f.ExecuteAsync(fault == "question" ? new string('问', 4097) : "问题", fault == "ids" ? [] : null));
        Assert.AreEqual(0, f.Budget.Requests.Count); Assert.AreEqual(0, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
    }
    [TestMethod] [DataRow("embedding")] [DataRow("chat")]
    public async Task Paid_answer_budget_rejection_and_duplicate_do_not_dispatch_or_settle_existing_operation(string stage)
    {
        foreach (var duplicate in new[] { false, true })
        {
            using var f = await PaidAnswerFixture.CreateAsync(false);
            f.Budget.RejectKind = duplicate ? null : stage; f.Budget.DuplicateKind = duplicate ? stage : null;
            await Assert.ThrowsExactlyAsync<AiBudgetException>(() => f.ExecuteAsync());
            Assert.AreEqual(stage == "embedding" ? 0 : 1, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
            Assert.AreEqual(stage == "embedding" ? 0 : 1, f.Budget.Settlements.Count);
        }
    }
    [TestMethod] [DataRow("embedding_create")] [DataRow("chat_create")] [DataRow("embedding_reserve")] [DataRow("chat_reserve")]
    public async Task Paid_answer_rechecks_authority_after_client_or_reservation_wait_before_dispatch(string stage)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false);
        if (stage.EndsWith("create", StringComparison.Ordinal)) f.Factory.BeforeCreate = kind => { if (stage.StartsWith(kind, StringComparison.Ordinal)) f.R.Denied = true; };
        else f.Budget.BeforeReserve = request => { if (stage.StartsWith(request.Kind, StringComparison.Ordinal)) f.R.Denied = true; };
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => f.ExecuteAsync());
        Assert.AreEqual(stage.StartsWith("embedding", StringComparison.Ordinal) ? 0 : 1, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
        Assert.AreEqual(new AiOperationUsage(0, 0), f.Budget.Settlements[^1].Usage);
    }
    [TestMethod] [DataRow("embedding")] [DataRow("chat")] [DataRow("embedding_settle")] [DataRow("chat_settle")]
    public async Task Paid_answer_revoke_after_paid_work_or_settlement_withholds_body_and_preserves_usage(string stage)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false);
        f.Factory.BeforeReturn = kind => { if (kind == stage) f.R.Denied = true; };
        f.Budget.BeforeSettle = request => { if (request.Kind + "_settle" == stage) f.R.Denied = true; };
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => f.ExecuteAsync());
        Assert.AreEqual(stage.StartsWith("embedding", StringComparison.Ordinal) ? 1 : 2, f.Budget.Settlements.Count);
        Assert.AreEqual(7, f.Budget.Settlements[^1].Usage.InputTokens);
    }
    [TestMethod] [DataRow("embedding")] [DataRow("chat")]
    public async Task Paid_answer_provider_throw_after_dispatch_keeps_unknown_usage_and_settles_independently(string stage)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); f.Factory.ThrowKind = stage;
        await Assert.ThrowsExactlyAsync<IOException>(() => f.ExecuteAsync());
        Assert.AreEqual(new AiOperationUsage(null, null), f.Budget.Settlements[^1].Usage);
        Assert.IsTrue(f.Budget.Settlements.All(s => !s.Token.IsCancellationRequested && s.Token.CanBeCanceled));
    }
    [TestMethod] [DataRow("citation")] [DataRow("tool")] [DataRow("role")] [DataRow("multiple")]
    [DataRow("usage")] [DataRow("usage_overflow")] [DataRow("size")] [DataRow("vectors")]
    public async Task Paid_answer_invalid_output_does_not_discard_known_charge(string fault)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); f.Factory.Fault = fault;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.ExecuteAsync());
        Assert.AreEqual(fault == "usage" ? null : 7, f.Budget.Settlements[^1].Usage.InputTokens);
        Assert.AreEqual("failed", f.Budget.Settlements[^1].Outcome);
    }
    [TestMethod] [DataRow("embedding")] [DataRow("chat")]
    public async Task Paid_answer_settlement_failure_does_not_deliver_success_or_start_next_stage(string stage)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); f.Budget.SettleFailureKind = stage;
        await Assert.ThrowsExactlyAsync<IOException>(() => f.ExecuteAsync());
        Assert.AreEqual(stage == "embedding" ? 0 : 1, f.Factory.ChatCalls);
    }
    [TestMethod] [DataRow("create")] [DataRow("generate")] [DataRow("settle")]
    public async Task Paid_answer_generation_destination_drift_is_rejected_at_wait_boundaries(string stage)
    {
        using var f = await PaidAnswerFixture.CreateAsync(true);
        void Drift() => f.R.Query.GenerationModel = PaidAnswerFixture.Model(f.Generation.Id, "https://changed.example/v1");
        f.Factory.BeforeCreate = kind => { if (kind == "chat" && stage == "create") Drift(); };
        f.Factory.BeforeReturn = kind => { if (kind == "chat" && stage == "generate") Drift(); };
        f.Budget.BeforeSettle = request => { if (request.Kind == "chat" && stage == "settle") Drift(); };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.ExecuteAsync());
        Assert.AreEqual(stage == "create" ? 0 : 1, f.Factory.ChatCalls);
    }
    [TestMethod]
    public async Task Paid_answer_cancellation_after_dispatch_still_settles_known_usage_without_body()
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); using var stop = new CancellationTokenSource();
        f.Factory.BeforeReturn = kind => { if (kind == "chat") stop.Cancel(); };
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.ExecuteAsync(token: stop.Token));
        Assert.AreEqual(7, f.Budget.Settlements[^1].Usage.InputTokens); Assert.AreEqual(3, f.Budget.Settlements[^1].Usage.OutputTokens);
        Assert.AreEqual("cancelled", f.Budget.Settlements[^1].Outcome); Assert.IsFalse(f.Budget.Settlements[^1].Token.IsCancellationRequested);
    }
    [TestMethod]
    public async Task Paid_answer_uncited_payload_withdrawal_still_withholds_answer_and_preserves_charge()
    {
        using var f = await PaidAnswerFixture.CreateAsync(false);
        f.Factory.BeforeReturn = kind => { if (kind == "chat") f.R.F.Payloads[1].Payload[0] ^= 1; };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.ExecuteAsync());
        Assert.AreEqual(7, f.Budget.Settlements[^1].Usage.InputTokens);
        Assert.AreEqual("failed", f.Budget.Settlements[^1].Outcome);
    }
    [TestMethod]
    public async Task Paid_answer_settles_original_tenant_when_request_scope_changes_during_generation()
    {
        using var f = await PaidAnswerFixture.CreateAsync(true); var tenantId = f.R.F.Tenant.Id;
        f.Factory.BeforeReturn = kind => { if (kind == "chat") f.R.F.Tenant.SetHost(); };
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => f.ExecuteAsync());
        Assert.AreEqual(2, f.Budget.SettlementTenants.Count);
        Assert.IsTrue(f.Budget.SettlementTenants.All(id => id == tenantId));
        Assert.AreEqual(7, f.Budget.Settlements[^1].Usage.InputTokens);
    }
    [TestMethod] [DataRow("embedding")] [DataRow("chat")]
    public async Task Paid_answer_client_creation_failure_releases_new_reservation_with_zero_usage(string stage)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); f.Factory.ThrowKind = stage + "_create";
        await Assert.ThrowsExactlyAsync<IOException>(() => f.ExecuteAsync());
        Assert.AreEqual(new AiOperationUsage(0, 0), f.Budget.Settlements[^1].Usage);
    }
    private sealed class PaidAnswerFixture : IDisposable
    {
        internal RetrievalFixture R = null!; internal AiModelConfigRecord Generation = null!;
        internal PaidAnswerBudget Budget = new(); internal PaidAnswerFactory Factory = new();
        internal readonly AiModelBindingScope bindings = new(); internal AiKnowledgePaidAnswerService Service = null!;
        private ServiceProvider services = null!;
        internal static async Task<PaidAnswerFixture> CreateAsync(bool tenant)
        {
            var f = new PaidAnswerFixture { R = await RetrievalFixture.Create(tenant) };
            f.Generation = Model(Guid.CreateVersion7()); f.R.Query.GenerationModel = f.Generation;
            f.services = new ServiceCollection().AddScoped<CurrentTenantAccessor>()
                .AddScoped<ICurrentTenantContextWriter>(s => s.GetRequiredService<CurrentTenantAccessor>())
                .AddScoped<IAiOperationBudgetStore>(s => new PaidSettlementBudget(f.Budget, s.GetRequiredService<CurrentTenantAccessor>())).BuildServiceProvider();
            f.UseFactories([f.Factory]); return f;
        }
        internal void UseFactories(IEnumerable<IAiModelClientFactory> factories)
        {
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
            Service = new(R.Service, new(R.Service, ids), R.Query, R.F.Transactions, R.F.Tenant,
                R.Permissions, Budget, factories, bindings, ids,
                new(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiChatCleanupScope>.Instance));
        }
        internal static AiModelConfigRecord Model(Guid id, string endpoint = "https://model.example/v1", string? credential = null) =>
            new() { Id = id, Version = 1, ProviderKey = "openai_compatible", ModelId = "gpt-4.1-mini", EndpointBaseUrl = endpoint, IsEnabled = true, ApiKeyProtected = credential };
        internal Task<AiKnowledgeExecutedAnswer> ExecuteAsync(string question = "问题", IReadOnlyList<Guid>? ids = null, CancellationToken token = default)
            => Service.ExecuteAsync(ids ?? [R.F.Binding.KnowledgeBaseId], R.Actor, R.Model, Generation, question, token);
        public void Dispose() { bindings.Dispose(); services.Dispose(); }
    }
    private sealed class PaidAnswerBudget : IAiOperationBudgetStore
    {
        internal List<AiOperationRequest> Requests = [];
        internal List<(AiOperationUsage Usage, string Outcome, CancellationToken Token)> Settlements = [];
        internal List<Guid?> SettlementTenants = [];
        internal string? RejectKind, DuplicateKind, SettleFailureKind;
        internal Action<AiOperationRequest>? BeforeReserve, BeforeSettle;
        public Task<AiOperationReservation> ReserveAsync(AiOperationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Requests.Add(request); BeforeReserve?.Invoke(request);
            if (RejectKind == request.Kind) throw new AiBudgetException("ai.budget.monthly_limit");
            return Task.FromResult(new AiOperationReservation(request.OperationId, DuplicateKind != request.Kind, "pending", request.InputTokenLimit + request.OutputTokenLimit, null, null));
        }
        public Task SettleAsync(Guid operationId, AiOperationUsage usage, string outcome, CancellationToken cancellationToken = default)
        {
            var request = Requests.Single(r => r.OperationId == operationId); BeforeSettle?.Invoke(request);
            if (SettleFailureKind == request.Kind) throw new IOException("结算失败");
            Settlements.Add((usage, outcome, cancellationToken)); return Task.CompletedTask;
        }
    }
    private sealed class PaidSettlementBudget(PaidAnswerBudget inner, CurrentTenantAccessor tenant) : IAiOperationBudgetStore
    {
        public Task<AiOperationReservation> ReserveAsync(AiOperationRequest request, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("独立清理作用域不能预留新推理。");
        public Task SettleAsync(Guid operationId, AiOperationUsage usage, string outcome, CancellationToken cancellationToken = default)
        { inner.SettlementTenants.Add(tenant.Id); return inner.SettleAsync(operationId, usage, outcome, cancellationToken); }
    }
    private sealed class PaidAnswerFactory : IAiModelClientFactory
    {
        public string ProviderKey => "openai_compatible";
        internal UsageDetails? Usage = new() { InputTokenCount = 7, OutputTokenCount = 3 };
        internal string? Fault, ThrowKind; internal int EmbeddingCalls, ChatCalls, StreamCalls;
        internal bool EmbeddingDisposed, ChatDisposed; internal Action<string>? BeforeCreate, BeforeReturn;
        internal string? BlockStage; internal TaskCompletionSource? BlockEntered; internal Task? BlockRelease;
        internal TaskCompletionSource LateDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<ChatMessage>? Messages; internal ChatOptions? Options;
        public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken cancellationToken)
        { BeforeCreate?.Invoke("embedding"); if (ThrowKind == "embedding_create") throw new IOException("创建失败"); return ValueTask.FromResult<IEmbeddingGenerator<string, Embedding<float>>>(new Generator(this)); }
        public async ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken)
        { BeforeCreate?.Invoke("chat"); if (ThrowKind == "chat_create") throw new IOException("创建失败");
            if (BlockStage == "create") { BlockEntered!.TrySetResult(); await BlockRelease!; } return new Client(this); }
        private sealed class Generator(PaidAnswerFactory f) : IEmbeddingGenerator<string, Embedding<float>>
        {
            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() => f.EmbeddingDisposed = true;
            public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested(); f.EmbeddingCalls++; Assert.AreEqual(1, values.Count()); f.BeforeReturn?.Invoke("embedding");
                if (f.ThrowKind == "embedding") throw new IOException("模型失败");
                var vector = AiKnowledgeRetrievalRuleTests.Vector(1, 0); if (f.Fault == "vectors") vector[0] = float.NaN;
                return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>([new(vector)])
                    { Usage = f.Usage is null ? null : new UsageDetails { InputTokenCount = 7, OutputTokenCount = 0 } });
            }
        }
        private sealed class Client(PaidAnswerFactory f) : IChatClient
        {
            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose()
            {
                // 独立后台线程中的同步释放门闩用于证明请求不会被第三方 Dispose 占住。
                if (f.BlockStage == "dispose") { f.BlockEntered!.TrySetResult(); f.BlockRelease!.GetAwaiter().GetResult(); }
                f.ChatDisposed = true; f.LateDisposed.TrySetResult();
            }
            public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested(); f.ChatCalls++; f.Messages = messages.ToList(); f.Options = options; f.BeforeReturn?.Invoke("chat");
                if (f.ThrowKind == "chat") throw new IOException("模型失败");
                if (f.BlockStage == "generate") { f.BlockEntered!.TrySetResult(); await f.BlockRelease!; }
                using var json = JsonDocument.Parse(f.Messages[1].Text); var id = json.RootElement.GetProperty("sources")[0].GetProperty("citationId").GetGuid();
                var text = $$"""{"answer":"有依据的答案","citationIds":["{{(f.Fault == "citation" ? Guid.CreateVersion7() : id)}}"],"refusal":false}""";
                if (f.Fault == "size") text = new string('x', 65537);
                if (f.Fault == "refusal") text = "{\"answer\":\"\",\"citationIds\":[],\"refusal\":true}";
                var content = f.Fault == "tool" ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call", "delete", new Dictionary<string, object?>())])
                    : new ChatMessage(f.Fault == "role" ? ChatRole.User : ChatRole.Assistant, text);
                var result = new ChatResponse(f.Fault == "multiple" ? [content, content] : [content])
                { Usage = f.Fault == "usage" ? new UsageDetails { InputTokenCount = -1, OutputTokenCount = 3 }
                    : f.Fault == "usage_overflow" ? new UsageDetails { InputTokenCount = 7, OutputTokenCount = 9000 } : f.Usage };
                return result;
            }
            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            { f.StreamCalls++; throw new AssertFailedException("问答不得逐 Token 输出。"); }
        }
    }
    private sealed class PaidAnswerHttpHandler : HttpMessageHandler
    {
        internal int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (request.RequestUri!.AbsolutePath.EndsWith("/embeddings", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                    { data = new[] { new { index = 0, embedding = AiKnowledgeRetrievalRuleTests.Vector(1, 0) } }, usage = new { prompt_tokens = 7, total_tokens = 7 } }), Encoding.UTF8, "application/json") };
            Assert.AreEqual(4096, payload.RootElement.GetProperty("max_tokens").GetInt32());
            Assert.IsFalse(payload.RootElement.TryGetProperty("tools", out _));
            using var context = JsonDocument.Parse(payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var id = context.RootElement.GetProperty("sources")[0].GetProperty("citationId").GetGuid();
            var answer = $$"""{"answer":"真实客户端答案","citationIds":["{{id}}"],"refusal":false}""";
            var frame = JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = answer } } }, usage = new { prompt_tokens = 7, completion_tokens = 3 } });
            return new(HttpStatusCode.OK) { Content = new StringContent("data: " + frame + "\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }
}
