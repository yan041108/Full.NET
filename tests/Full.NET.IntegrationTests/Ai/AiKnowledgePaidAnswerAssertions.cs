using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>消费真实索引、来源 SQL 和生产预算账本；模型与读取身份受控，不证明云质量。</summary>
internal static class AiKnowledgePaidAnswerAssertions
{
    internal static async Task<long> VerifyAsync(IServiceProvider services, DbConnection db, AiKnowledgeRetrievalService retrieval,
        AiKnowledgePermissionGuard permissions, AiModelConfigRecord embedding, Guid knowledgeBaseId, Guid actor, Action<bool> deny)
    {
        var query = services.GetRequiredService<IQueryExecutor>(); var tenant = services.GetRequiredService<ICurrentTenant>();
        var ids = services.GetRequiredService<IIdGenerator>(); var generationId = ids.NewId();
        // 夹具显式创建另一生成配置，既不改动原索引模型，也不使用外部服务或生产密钥。
        await db.ExecuteAsync("INSERT INTO fn_ai_model_config (Id,TenantId,Name,ProviderKey,EndpointBaseUrl,ModelId,IsDefault,IsEnabled,CreatedAtUtc,Version) VALUES (@Id,@TenantId,'问答生成测试',@Provider,@Endpoint,'gpt-4.1-mini',0,1,@Now,1)",
            new { Id = generationId, TenantId = (Guid?)null, Provider = embedding.ProviderKey, Endpoint = embedding.EndpointBaseUrl, Now = DateTimeOffset.UtcNow });
        var generation = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(tenant.Id.HasValue ? AiModelConfigSql.FindAvailableForTenantChat
            : AiModelConfigSql.FindAvailableForHostChat, new Dictionary<string, object?> { ["ModelConfigId"] = generationId }); Assert.IsNotNull(generation);
        using var bindings = new AiModelBindingScope();
        var budget = new RecordingBudget(services.GetRequiredService<IAiOperationBudgetStore>());
        var factory = new ControlledFactory(); long embeddingCharges = 0;
        var service = new AiKnowledgePaidAnswerService(retrieval, new(retrieval, ids), query, services.GetRequiredService<IDataTransactionState>(),
            tenant, permissions, budget, [factory], bindings, ids, services.GetRequiredService<AiChatCleanupScope>());
        var originalPolicy = await db.QuerySingleAsync<(Guid? GenerationModelConfigId, int? GenerationModelVersion, string DataClassification)>(
            "SELECT GenerationModelConfigId,GenerationModelVersion,DataClassification FROM fn_ai_knowledge_base WHERE Id=@Id", new { Id = knowledgeBaseId });
        // 索引已由夹具准备；只在测试库显式设置同版本批准，以覆盖正常 API 不会产生的漂移窗口。
        foreach (var fault in new[] { "missing", "model", "version" })
        {
            await Approve(fault == "missing" ? null : fault == "model" ? embedding.Id : generationId, fault == "missing" ? null : fault == "version" ? 2 : 1);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => RunAsync());
            Assert.AreEqual(0, budget.Requests.Count);
            Assert.AreEqual(0, factory.EmbeddingCalls);
            Assert.AreEqual(0, factory.ChatCalls);
        }
        await Approve(generationId, 1);
        var policySnapshot = await retrieval.PreflightAsync([knowledgeBaseId], actor, embedding, default, generation);
        try
        {
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET DataClassification=@Classification WHERE Id=@Id",
                new { Id = knowledgeBaseId, Classification = originalPolicy.DataClassification == "restricted" ? "internal" : "restricted" });
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => retrieval.RevalidateAsync(policySnapshot, default));
            Assert.AreEqual(0, budget.Requests.Count);
        }
        finally
        {
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET DataClassification=@Classification WHERE Id=@Id",
                new { Id = knowledgeBaseId, Classification = originalPolicy.DataClassification });
        }
        foreach (var scenario in new[] { "known", "unknown", "revoke", "citation", "destination", "credential", "approval" })
        {
            budget.Requests.Clear(); factory.Unknown = scenario == "unknown"; factory.Forge = scenario == "citation";
            factory.BeforeChatReturn = scenario switch
            {
                "revoke" => () => { deny(true); return Task.CompletedTask; },
                "destination" => async () => { await db.ExecuteAsync("UPDATE fn_ai_model_config SET EndpointBaseUrl=@Endpoint WHERE Id=@Id", new { Id = generationId, Endpoint = generation.EndpointBaseUrl + " " }); },
                "credential" => async () => { await db.ExecuteAsync("UPDATE fn_ai_model_config SET ApiKeyProtected='changed-protected' WHERE Id=@Id", new { Id = generationId }); },
                "approval" => () => Approve(null, null),
                _ => null
            };
            try
            {
                if (scenario == "revoke") await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => RunAsync());
                else if (scenario is "citation" or "destination" or "credential" or "approval") await Assert.ThrowsExactlyAsync<InvalidDataException>(() => RunAsync());
                else
                {
                    using var stages = new LocalStageCapture();
                    var result = await RunAsync(); Assert.AreEqual("真实索引受控问答", result.Answer.Text);
                    stages.Verify();
                    Assert.IsTrue(result.Prepared.Retrieval.Chunks.Count > result.Answer.Citations.Count);
                    Assert.AreEqual(generationId, result.Prepared.Retrieval.GenerationModel!.Id);
                    Assert.IsTrue(result.Prepared.Context.Citations.Count > 0);
                    Assert.IsTrue(result.Prepared.Context.Citations.All(c => c.Chunk.Length == c.Chunk.Text.Length));
                }
                Assert.AreEqual(2, budget.Requests.Count);
                foreach (var request in budget.Requests)
                {
                    var row = await query.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find,
                        new Dictionary<string, object?> { ["OperationId"] = request.OperationId, ["ScopeKey"] = tenant.Id?.ToString("N") ?? "host" });
                    Assert.IsNotNull(row); Assert.AreEqual(request.ModelConfigId, row.ModelConfigId);
                    Assert.AreEqual(scenario == "unknown" ? "unknown" : "known", row.UsageStatus);
                    Assert.AreEqual(scenario == "unknown" ? null : 7L, row.InputTokens);
                    Assert.AreEqual(scenario == "unknown" ? null : request.Kind == "embedding" ? 0L : 3L, row.OutputTokens);
                    Assert.IsTrue(row.ReservedTokens > 0);
                    var expectedCharge = scenario == "unknown" ? row.ReservedTokens : request.Kind == "embedding" ? 7L : 10L;
                    Assert.AreEqual(expectedCharge, await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = request.OperationId }));
                    if (request.Kind == "embedding") embeddingCharges = checked(embeddingCharges + expectedCharge);
                    Assert.AreEqual(request.Kind == "embedding" || scenario is "known" or "unknown" ? "succeeded"
                        : scenario == "revoke" ? "cancelled" : "failed", row.Outcome);
                }
            }
            finally
            {
                deny(false); factory.BeforeChatReturn = null;
                await Approve(generationId, 1);
                await db.ExecuteAsync("UPDATE fn_ai_model_config SET EndpointBaseUrl=@Endpoint,ApiKeyProtected=@Credential WHERE Id=@Id",
                    new { Id = generationId, Endpoint = generation.EndpointBaseUrl, Credential = generation.ApiKeyProtected });
            }
        }
        // 从另一入口租户等待共享 Host 模型时切回实际可读范围，也必须先拒绝，不能预留后按旧租户结算。
        var writer = services.GetRequiredService<ICurrentTenantContextWriter>();
        var original = tenant.Id is { } originalId ? new TenantContext(originalId, tenant.Identifier!, tenant.Name!) : null;
        void Restore() { if (original is null) writer.SetHost(); else writer.SetTenant(original); }
        budget.Requests.Clear();
        var changingQuery = new ScopeChangingQuery(query, generationId, Restore);
        var changing = new AiKnowledgePaidAnswerService(retrieval, new(retrieval, ids), changingQuery,
            services.GetRequiredService<IDataTransactionState>(), tenant, permissions, budget, [factory], bindings, ids,
            services.GetRequiredService<AiChatCleanupScope>());
        try
        {
            writer.SetTenant(new(ids.NewId(), "entry", "入口租户"));
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => changing.ExecuteAsync([knowledgeBaseId], actor, embedding, generation, "来源说明", default));
            Assert.AreEqual(0, budget.Requests.Count);
        }
        finally { Restore(); }
        await Approve(originalPolicy.GenerationModelConfigId, originalPolicy.GenerationModelVersion);
        return embeddingCharges;
        Task<AiKnowledgeExecutedAnswer> RunAsync() => service.ExecuteAsync([knowledgeBaseId], actor, embedding, generation, "来源说明", default);
        Task Approve(Guid? id, int? version) => db.ExecuteAsync(
            "UPDATE fn_ai_knowledge_base SET GenerationModelConfigId=@ModelId,GenerationModelVersion=@ModelVersion WHERE Id=@Id",
            new { Id = knowledgeBaseId, ModelId = id, ModelVersion = version });
    }
    /// <summary>真实账本和索引流程中的本地阶段；只捕获当前追踪下的固定标签，不读取业务载荷。</summary>
    private sealed class LocalStageCapture : IDisposable
    {
        private readonly Activity root = new Activity("test.knowledge.local").SetIdFormat(ActivityIdFormat.W3C).Start();
        private readonly List<Activity> spans = [];
        private readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> values = [];
        private readonly MeterListener meter = new();
        private readonly ActivityListener listener;
        internal LocalStageCapture()
        {
            meter.InstrumentPublished = (instrument, capture) => { if (instrument.Meter.Name == Full.NET.Agents.Runtime.AgentExecutionTelemetry.Name) capture.EnableMeasurementEvents(instrument); };
            meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
            meter.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
            meter.Start();
            listener = new ActivityListener { ShouldListenTo = source => source.Name == Full.NET.Agents.Runtime.AgentExecutionTelemetry.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = span => { if (span.TraceId == root.TraceId && Local(span.GetTagItem("ai.stage"))) spans.Add(span); } };
            ActivitySource.AddActivityListener(listener);
        }
        private static bool Local(object? stage) => stage is "knowledge_rerank" or "knowledge_context";
        private void Record(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        { if (Activity.Current?.TraceId == root.TraceId && tags.ToArray().Any(t => t.Key == "ai.stage" && Local(t.Value))) values.Add((name, value, tags.ToArray())); }
        internal void Verify()
        {
            Assert.AreEqual(2, spans.Count); Assert.AreEqual(4, values.Count);
            CollectionAssert.AreEquivalent(new[] { "knowledge_rerank", "knowledge_context" }, spans.Select(s => (string)s.GetTagItem("ai.stage")!).ToArray());
            foreach (var span in spans) { Assert.AreEqual("completed", span.GetTagItem("ai.outcome")); Assert.IsTrue(span.TagObjects.All(t => t.Key is "ai.stage" or "ai.outcome")); Assert.IsNull(span.StatusDescription); Assert.IsFalse(span.Events.Any()); }
            foreach (var value in values) { CollectionAssert.AreEquivalent(new[] { "ai.stage", "ai.outcome" }, value.Tags.Select(t => t.Key).ToArray()); Assert.IsTrue(value.Name.EndsWith("attempts", StringComparison.Ordinal) ? value.Value == 1 : value.Value >= 0); }
            Assert.AreSame(root, Activity.Current);
        }
        public void Dispose() { listener.Dispose(); meter.Dispose(); root.Dispose(); }
    }
    private sealed class ScopeChangingQuery(IQueryExecutor inner, Guid generationId, Action change) : IQueryExecutor
    {
        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var row = await inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
            if (typeof(T) == typeof(AiModelConfigRecord) && parameters is IReadOnlyDictionary<string, object?> values
                && (Guid)values["ModelConfigId"]! == generationId) change();
            return row;
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => inner.QueryAsync<T>(statement, parameters, cancellationToken);
    }
    private sealed class RecordingBudget(IAiOperationBudgetStore inner) : IAiOperationBudgetStore
    {
        internal List<AiOperationRequest> Requests = [];
        public async Task<AiOperationReservation> ReserveAsync(AiOperationRequest request, CancellationToken cancellationToken = default)
        { var reservation = await inner.ReserveAsync(request, cancellationToken); Requests.Add(request); return reservation; }
        public Task SettleAsync(Guid operationId, AiOperationUsage usage, string outcome, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("结算必须由独立原租户作用域消费生产账本。");
    }
    internal sealed class ControlledFactory : IAiModelClientFactory
    {
        public string ProviderKey => "openai_compatible";
        internal bool Unknown, Forge, Refused; internal Func<Task>? BeforeChatReturn;
        internal int EmbeddingCalls, ChatCalls;
        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken)
            => ValueTask.FromResult<IChatClient>(new Client(this));
        public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken cancellationToken)
            => ValueTask.FromResult<IEmbeddingGenerator<string, Embedding<float>>>(new Generator(this, binding.ModelId == "text-embedding-3-small" ? 1536 : 3072));
        private sealed class Generator(ControlledFactory f, int dimensions) : IEmbeddingGenerator<string, Embedding<float>>
        {
            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
            public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested(); f.EmbeddingCalls++; Assert.AreEqual(1, values.Count()); var vector = new float[dimensions]; vector[0] = 1;
                return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>([new(vector)])
                    { Usage = f.Unknown ? null : new UsageDetails { InputTokenCount = 7, OutputTokenCount = 0 } });
            }
        }
        private sealed class Client(ControlledFactory f) : IChatClient
        {
            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
            public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested(); f.ChatCalls++; Assert.IsNull(options);
                using var json = JsonDocument.Parse(messages.Last().Text); var source = json.RootElement.GetProperty("sources")[0];
                var id = f.Forge ? Guid.CreateVersion7() : source.GetProperty("citationId").GetGuid();
                if (f.BeforeChatReturn is not null) await f.BeforeChatReturn();
                var text = f.Refused ? "{\"answer\":\"\",\"citationIds\":[],\"refusal\":true}" : $$"""{"answer":"真实索引受控问答","citationIds":["{{id}}"],"refusal":false}""";
                return new(new ChatMessage(ChatRole.Assistant, text))
                { Usage = f.Unknown ? null : new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3 } };
            }
            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
                => throw new AssertFailedException("收费问答不得逐 Token 交付。");
        }
    }
}
