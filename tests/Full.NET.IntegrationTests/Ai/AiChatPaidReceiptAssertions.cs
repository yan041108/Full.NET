using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Streaming;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库会话、生成租约和原预算Store配合受控SDK，核对消费及凭据所有权。</summary>
internal static class AiChatPaidReceiptAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, DbConnection db, Guid model)
    {
        // 使用生产开通契约获得真实活动租户，独立续租和收尾继续通过权威解析器。
        Guid tenant;
        await using (var provision = factory.Services.CreateAsyncScope())
        {
            var context = provision.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>(); context.SetHost();
            var key = "receipt-" + Guid.CreateVersion7().ToString("N");
            var created = await provision.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(new(key, "聊天回执", key + ".localhost"));
            Assert.IsTrue(created.IsSuccess); tenant = created.Value!.Id; context.Clear();
        }
        // 未知消费保留预留，各场景的足额配额不得互相污染。
        var now = factory.Services.GetRequiredService<IClock>().UtcNow;
        await db.ExecuteAsync("""
            INSERT INTO fn_ai_tenant_quota (Id,TenantId,MonthlyTokenLimit,MonthlyRequestLimit,UsedTokensThisMonth,UsedRequestsThisMonth,QuotaMonthKey,IsEnabled,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@Tenant,1000000,100,0,0,@Month,1,@Now,@Now,1)
            """, new { Id = Guid.CreateVersion7(), Tenant = tenant, Month = now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), Now = now });
        foreach (var scoped in new[] { false, true })
        foreach (var fault in new[] { "good", "factory", "dispatch", "dispose", "invalid", "duplicate", "identity" })
        {
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ICurrentTenantContextWriter>();
            if (scoped) context.SetTenant(new(tenant, "chat-receipt", "聊天回执")); else context.SetHost();
            var session = Guid.CreateVersion7(); var owner = Guid.CreateVersion7(); var generation = Guid.CreateVersion7();
            await db.ExecuteAsync("""
                INSERT INTO fn_ai_chat_session (Id,TenantId,OwnerUserId,ModelConfigId,ModelName,Title,MessageCount,IsGenerating,CreatedAtUtc,Version)
                VALUES (@Id,@Tenant,@Owner,@Model,'budget test','receipt',0,0,@Now,1)
                """, new { Id = session, Tenant = scoped ? tenant : (Guid?)null, Owner = owner, Model = model, Now = services.GetRequiredService<IClock>().UtcNow.UtcDateTime });
            using var bindings = new AiModelBindingScope(); var provider = new Provider(fault);
            var budget = new Budget(services.GetRequiredService<IAiOperationBudgetStore>(), fault);
            var output = new Output(); var registry = new AiChatGenerationRegistry();
            var clock = services.GetRequiredService<IClock>(); var scopes = services.GetRequiredService<IServiceScopeFactory>();
            var service = new AiChatStreamService(services.GetRequiredService<IQueryExecutor>(), services.GetRequiredService<ICommandExecutor>(),
                services.GetRequiredService<ICommandTransaction>(), services.GetRequiredService<AiChatSessionQueryService>(), new([provider], bindings), registry,
                new(scopes, clock, NullLogger<AiChatGenerationLeaseMonitor>.Instance), budget,
                new(scopes, NullLogger<AiChatCleanupScope>.Instance), services.GetRequiredService<ICurrentTenant>(),
                services.GetRequiredService<IOptions<DatabaseOptions>>(), clock, new GenerationIds(generation));
            using var telemetry = new AiOperationStageTelemetryAssertions();
            await service.StreamAsync(session, owner, new("billing probe"), output);
            var request = budget.Request!; Assert.IsNotNull(request); Assert.AreEqual(generation, request.OperationId);
            var rejected = fault is "duplicate" or "identity";
            telemetry.Verify("chat", rejected ? "budget_rejected" : fault == "good" ? "completed" : "failed", rejected ? null : generation);
            Assert.AreEqual(rejected || fault == "factory" ? 0 : 1, provider.Calls, fault);
            if (provider.Calls == 1)
            {
                // 真实账本预留必须与实际SDK收到的历史一致，不用重新读取消息替代派发输入。
                var selectedModel = await services.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                    scoped ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
                    AiSqlParameters.Create(("ModelConfigId", model)), CancellationToken.None);
                Assert.IsNotNull(selectedModel); Assert.IsNotNull(provider.Captured);
                Assert.AreEqual(request, AiChatBudgetRequest.Create(generation, selectedModel, provider.Captured));
                CollectionAssert.AreEqual(new[] { ("user", "billing probe") }, provider.Captured);
            }
            Assert.AreEqual(!rejected, output.HasStarted, fault);
            var row = await db.QuerySingleAsync<Row>("""
                SELECT ScopeKey,UsageStatus,Outcome,InputTokens,OutputTokens,CachedInputTokens,ReservedTokens,ChargedTokens,ChargedCost
                FROM fn_ai_operation_budget WHERE Id=@Id
                """, new { Id = generation });
            Assert.AreEqual(scoped ? tenant.ToString("N") : "host", row.ScopeKey, fault);
            if (rejected)
            {
                Assert.AreEqual("reserved", row.UsageStatus, fault); Assert.AreEqual("pending", row.Outcome, fault);
                Assert.AreEqual(row.ReservedTokens, row.ChargedTokens, fault); Assert.IsFalse(output.Done);
            }
            else if (fault is "dispatch" or "invalid")
            {
                Assert.AreEqual("unknown", row.UsageStatus, fault); Assert.AreEqual("failed", row.Outcome, fault);
                Assert.IsNull(row.InputTokens); Assert.IsNull(row.OutputTokens);
                Assert.AreEqual(row.ReservedTokens, row.ChargedTokens); Assert.IsFalse(output.Done);
            }
            else
            {
                var zero = fault == "factory";
                Assert.AreEqual("known", row.UsageStatus); Assert.AreEqual(fault == "good" ? "succeeded" : "failed", row.Outcome);
                Assert.AreEqual(zero ? 0L : 7L, row.InputTokens); Assert.AreEqual(zero ? 0L : 3L, row.OutputTokens);
                Assert.AreEqual(zero ? null : 2L, row.CachedInputTokens); Assert.AreEqual(zero ? 0L : 10L, row.ChargedTokens);
                Assert.AreEqual(zero ? 0m : 0.000024m, row.ChargedCost); Assert.AreEqual(fault == "good", output.Done);
            }
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_chat_session WHERE Id=@Id AND (IsGenerating<>0 OR GenerationId IS NOT NULL)", new { Id = session }));
            Assert.IsFalse(registry.TryCancel(session, generation)); context.Clear();
        }
    }

    private sealed class GenerationIds(Guid generation) : IIdGenerator
    { private int count; public Guid NewId() => ++count == 2 ? generation : Guid.CreateVersion7(); }
    private sealed class Budget(IAiOperationBudgetStore inner, string fault) : IAiOperationBudgetStore
    {
        internal AiOperationRequest? Request;
        public async Task<AiOperationReservation> ReserveAsync(AiOperationRequest request, CancellationToken token = default)
        {
            Request = request; var receipt = await inner.ReserveAsync(request, token);
            if (fault == "duplicate") return await inner.ReserveAsync(request, token);
            // 故障仅改变返回凭据；原请求的真实账本必须保持预留，不以错误凭据执行结算。
            return fault == "identity" ? receipt with { OperationId = Guid.CreateVersion7() } : receipt;
        }
        public Task SettleAsync(Guid id, AiOperationUsage usage, string outcome, CancellationToken token = default)
        => throw new AssertFailedException("收尾必须使用独立作用域预算Store");
    }
    private sealed class Provider(string fault) : IAiModelClientFactory
    {
        public string ProviderKey => "ollama"; internal int Calls;
        internal (string RoleKey, string Content)[]? Captured;
        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken token = default)
        { if (fault == "factory") throw new IOException("controlled factory failure"); return ValueTask.FromResult<IChatClient>(new Client(this, fault)); }
        public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken token = default) => throw new AssertFailedException();
        private sealed class Client(Provider owner, string fault) : IChatClient
        {
            public object? GetService(Type type, object? key = null) => null;
            public void Dispose() { if (fault == "dispose") throw new IOException("controlled disposal failure"); }
            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken token = default) => throw new AssertFailedException();
            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken token = default)
            { owner.Calls++; owner.Captured = messages.Select(m => (m.Role.Value, m.Text ?? string.Empty)).ToArray(); return Updates(); }
            private async IAsyncEnumerable<ChatResponseUpdate> Updates()
            {
                await Task.CompletedTask; if (fault == "dispatch") throw new IOException("controlled dispatch failure");
                yield return new() { Contents = [new TextContent("billing result"), new UsageContent(new()
                    { InputTokenCount = 7, OutputTokenCount = 3, CachedInputTokenCount = fault == "invalid" ? 8 : 2, TotalTokenCount = 10 })] };
            }
        }
    }
    private sealed class Output : IAiChatOutput
    {
        public bool HasStarted { get; private set; } internal bool Done;
        public Task StartAsync(CancellationToken token) { HasStarted = true; return Task.CompletedTask; }
        public Task WriteDeltaAsync(string text, CancellationToken token) => Task.CompletedTask;
        public Task WriteErrorAsync(string text, CancellationToken token) => Task.CompletedTask;
        public Task WriteDoneAsync(Guid id, int? input, int? output, CancellationToken token) { Done = true; return Task.CompletedTask; }
    }
    private sealed class Row
    {
        public string ScopeKey { get; init; } = ""; public string UsageStatus { get; init; } = ""; public string Outcome { get; init; } = "";
        public long? InputTokens { get; init; } public long? OutputTokens { get; init; } public long? CachedInputTokens { get; init; }
        public long ReservedTokens { get; init; } public long ChargedTokens { get; init; } public decimal? ChargedCost { get; init; }
    }
}
