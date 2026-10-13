using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Features.TestEmbeddings;
using Full.NET.Modules.Ai.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>Host生产服务和预算账本联验；租户必须被原HostOnly查询拒绝，不冒充HTTP权限验收。</summary>
internal static class AiEmbeddingTestBudgetAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, DbConnection db, Guid model, Guid tenant)
    {
        foreach (var scoped in new[] { false, true })
        foreach (var fault in scoped ? new[] { "scope" } : new[] { "binding", "factory", "dispatch", "vectors", "good" })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if (scoped) context.SetTenant(new(tenant, "embedding-budget", "预算测试")); else context.SetHost();
            try
            {
                using var binding = new AiModelBindingScope(); if (fault == "binding") binding.Dispose();
                var budget = new BudgetProbe(scope.ServiceProvider.GetRequiredService<IAiOperationBudgetStore>());
                var provider = new ControlledFactory(fault);
                var service = new AiEmbeddingTestService(scope.ServiceProvider.GetRequiredService<IQueryExecutor>(),
                    [provider], binding, budget, scope.ServiceProvider.GetRequiredService<IIdGenerator>());
                using var telemetry = new AiOperationStageTelemetryAssertions();
                if (scoped)
                {
                    // 管理查询只准Host；不能为验证租户费用而放宽原查询，或绕过它伪装成生产路径。
                    await Assert.ThrowsExactlyAsync<HostContextRequiredException>(() => service.TestAsync(model, new("billing probe", null)));
                    telemetry.Verify("embedding_test", "authorization_required", null);
                    Assert.AreEqual(0, budget.Reserves); Assert.AreEqual(0, budget.Settlements);
                    Assert.AreEqual(0, provider.Creates); Assert.AreEqual(0, provider.Calls); continue;
                }
                var result = await service.TestAsync(model, new("billing probe", null));
                Assert.AreEqual(fault == "good", result.IsSuccess);
                var operation = budget.Request!; Assert.IsNotNull(operation); Assert.AreEqual(1, budget.Reserves); Assert.AreEqual(1, budget.Settlements);
                telemetry.Verify("embedding_test", fault == "good" ? "completed" : "failed", operation.OperationId);
                Assert.AreEqual(fault is "binding" or "factory" ? 0 : 1, provider.Calls);
                var state = await db.QuerySingleAsync<Row>("""
                    SELECT ScopeKey,UsageStatus,Outcome,InputTokens,OutputTokens,ReservedTokens,ChargedTokens
                    FROM fn_ai_operation_budget WHERE Id=@Id
                    """, new { Id = operation.OperationId });
                Assert.AreEqual("host", state.ScopeKey);
                Assert.AreEqual(fault == "dispatch" ? "unknown" : "known", state.UsageStatus);
                Assert.AreEqual(fault == "good" ? "succeeded" : "failed", state.Outcome);
                Assert.AreEqual(operation.InputTokenLimit, state.ReservedTokens);
                Assert.AreEqual(fault == "dispatch" ? operation.InputTokenLimit : fault is "binding" or "factory" ? 0L : 2L, state.ChargedTokens);
                Assert.AreEqual(fault == "dispatch" ? null : fault is "binding" or "factory" ? 0L : 2L, state.InputTokens);
                Assert.AreEqual(fault == "dispatch" ? null : 0L, state.OutputTokens);
            }
            finally { context.Clear(); }
        }
    }
    private sealed class Row
    {
        public string ScopeKey { get; set; } = "";
        public string UsageStatus { get; set; } = "";
        public string Outcome { get; set; } = "";
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public long ReservedTokens { get; set; }
        public long ChargedTokens { get; set; }
    }
    private sealed class BudgetProbe(IAiOperationBudgetStore inner) : IAiOperationBudgetStore
    {
        internal AiOperationRequest? Request; internal int Reserves, Settlements;
        public Task<AiOperationReservation> ReserveAsync(AiOperationRequest request, CancellationToken cancellationToken = default)
        { Reserves++; Request = request; return inner.ReserveAsync(request, cancellationToken); }
        public Task SettleAsync(Guid operationId, AiOperationUsage usage, string outcome, CancellationToken cancellationToken = default)
        {
            Settlements++; Assert.AreEqual(CancellationToken.None, cancellationToken);
            return inner.SettleAsync(operationId, usage, outcome, cancellationToken);
        }
    }
    private sealed class ControlledFactory(string fault) : IAiModelClientFactory, IEmbeddingGenerator<string, Embedding<float>>
    {
        public string ProviderKey => "ollama";
        internal int Creates, Calls;
        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken cancellationToken)
        { Creates++; if (fault == "factory") throw new IOException("客户端尚未创建"); return ValueTask.FromResult<IEmbeddingGenerator<string, Embedding<float>>>(this); }
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++; CollectionAssert.AreEqual(new[] { "billing probe" }, values.ToArray());
            if (fault == "dispatch") throw new IOException("派发结果未确认");
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(fault == "vectors" ? [] : [new(new float[] { 1 })])
                { Usage = new() { InputTokenCount = 2 } });
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
