using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试分片：AiKnowledgeIndexPdfActivation。</summary>
public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod]
    [DataRow(false, "success")]
    [DataRow(true, "success")]
    [DataRow(false, "refusal")]
    [DataRow(true, "refusal")]
    [DataRow(false, "citation")]
    [DataRow(true, "citation")]
    [DataRow(false, "cancel")]
    [DataRow(true, "cancel")]
    [DataRow(false, "registry_cancel")]
    [DataRow(true, "registry_cancel")]
    [DataRow(false, "late_revoke")]
    [DataRow(true, "late_revoke")]
    [DataRow(false, "late_classification")]
    [DataRow(true, "late_classification")]
    [DataRow(false, "late_approval")]
    [DataRow(true, "late_approval")]
    [DataRow(false, "cleanup")]
    [DataRow(true, "cleanup")]
    [DataRow(false, "lost")]
    [DataRow(true, "lost")]
    public async Task Ask_buffers_and_saves_before_cleanup_then_revalidates(bool scoped, string scenario)
    {
        using var f = await PaidAnswerFixture.CreateAsync(scoped);
        // 历史落库要求实际派生数据标记；旧检索夹具只模拟召回，此处补齐消息消费所需事实。
        f.R.Query.Rows = f.R.Query.Rows.Select(row => row with { HasDerivedData = 1 }).ToArray();
        f.R.F.ReadRow = f.R.Query.Rows[0];
        if (scenario is "refusal" or "citation") f.Factory.Fault = scenario;
        var actor = f.R.Actor; var tenant = f.R.F.Tenant; var sessionId = Guid.CreateVersion7();
        var query = new AskQuery(f.R.Query, new AiChatSessionRecord { Id = sessionId, OwnerUserId = actor, TenantId = tenant.Id,
            ModelConfigId = f.Generation.Id, Title = "新对话" }, () => f.R.Query.Rows);
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var generation = Guid.CreateVersion7();
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(generation, Guid.CreateVersion7());
        var roles = Substitute.For<IActiveRoleDirectory>(); var units = Substitute.For<ICurrentTenantOrganizationUnitDirectory>();
        roles.ListActiveRoleIdsForUserAsync(actor, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        units.ListActiveUnitIdsForUserAsync(actor, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        var options = Options.Create(new DatabaseOptions());
        var guard = new AiKnowledgeAnswerHistoryGuard(query, f.R.F.Transactions, tenant, options, new(roles, units, tenant), f.R.Permissions, new AskProof());
        var writes = new List<SqlStatement>(); var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var sql = call.Arg<SqlStatement>()!; writes.Add(sql);
            if (sql == AiChatGenerationSql.Renew && scenario == "lost") return 0;
            if (sql == AiChatGenerationSql.Release && scenario == "late_revoke") f.R.DeniedPermission = AiKnowledgeDocumentPermissions.SourceRead;
            if (sql == AiChatGenerationSql.Release && scenario == "late_classification")
                f.R.Query.Rows = [f.R.Query.Rows[0] with { DataClassification = "restricted" }];
            if (sql == AiChatGenerationSql.Release && scenario == "late_approval")
                f.R.Query.Rows = [f.R.Query.Rows[0] with { GenerationModelVersion = f.Generation.Version + 1 }];
            if (sql == AiChatGenerationSql.Release && scenario == "cleanup") throw new IOException("收尾不可用");
            return 1;
        });
        var expectedTenant = tenant.Id;
        var resolver = Substitute.For<IActiveTenantContextResolver>();
        if (expectedTenant is { } tenantId) resolver.ResolveActiveByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new TenantContext(tenantId, "test", "测试租户"));
        await using var services = new ServiceCollection().AddScoped<CurrentTenantAccessor>()
            .AddScoped<ICurrentTenantContextWriter>(s => s.GetRequiredService<CurrentTenantAccessor>())
            .AddSingleton(resolver).AddScoped<ICommandExecutor>(s =>
            { Assert.AreEqual(expectedTenant, s.GetRequiredService<CurrentTenantAccessor>().Id); return commands; }).BuildServiceProvider();
        var scopes = services.GetRequiredService<IServiceScopeFactory>(); var registry = new AiChatGenerationRegistry();
        using var stop = new CancellationTokenSource();
        if (scenario == "cancel") f.Factory.BeforeReturn = kind => { if (kind == "chat") stop.Cancel(); };
        if (scenario == "registry_cancel") f.Factory.BeforeReturn = kind => { if (kind == "chat") Assert.IsTrue(registry.TryCancel(sessionId, generation)); };
        var transaction = new AskTransaction();
        var service = new AiKnowledgeAskService(query, commands, transaction, f.R.F.Transactions, new(query, tenant, options, clock), f.Service,
            new(commands, query, transaction, tenant, clock, options, guard), guard, f.R.Permissions, registry,
            new(scopes, clock, NullLogger<AiChatGenerationLeaseMonitor>.Instance), new(scopes, NullLogger<AiChatCleanupScope>.Instance), tenant, clock, ids);
        if (scenario == "cancel")
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.AskAsync(sessionId, actor, new([f.R.F.Binding.KnowledgeBaseId], f.R.Model.Id, "问题"), stop.Token));
        else
        {
            var result = await service.AskAsync(sessionId, actor, new([f.R.F.Binding.KnowledgeBaseId], f.R.Model.Id, "问题"));
            Assert.AreEqual(scenario is "success" or "refusal", result.IsSuccess,
                $"错误码：{result.Error?.Code}；收费操作：{f.Budget.Requests.Count}；写入：{string.Join(',', writes.Select(sql => sql.Name))}");
            if (result.IsSuccess)
            {
                Assert.AreEqual(scenario == "refusal", result.Value!.Refused);
                Assert.AreEqual(scenario == "refusal" ? "" : "有依据的答案", result.Value.Message.Content);
                Assert.AreEqual(scenario == "refusal" ? 0 : 1, result.Value.Message.Citations.Count);
                Assert.IsNull(result.Value.Message.KnowledgeUnavailableCode);
            }
            if (scenario == "registry_cancel") Assert.AreEqual(AiErrorCodes.ChatGenerationNotActive, result.Error!.Code);
        }
        Assert.IsTrue(writes.Any(sql => sql == AiChatGenerationSql.Release));
        Assert.IsFalse(registry.TryCancel(sessionId, generation));
        if (scenario == "lost") Assert.AreEqual(0, f.Budget.Requests.Count);
        else { Assert.AreEqual(2, f.Budget.Requests.Count); Assert.AreEqual(2, f.Budget.Settlements.Count); }
        Assert.AreEqual(0, f.Factory.StreamCalls);
        var completed = writes.FindIndex(sql => sql == AiKnowledgeAnswerSql.CompleteFor(scoped));
        var release = writes.FindIndex(sql => sql == AiChatGenerationSql.Release);
        if (scenario is "success" or "refusal" or "late_revoke" or "late_classification" or "late_approval" or "cleanup") Assert.IsTrue(completed >= 0 && completed < release);
        else Assert.AreEqual(-1, completed);
    }

    private sealed class AskProof : IAiKnowledgeAnswerSourceProof
    {
        public Task<bool> VerifyAsync(AiKnowledgeIndexStatusRecord source, Dictionary<string, object?> parameters, CancellationToken token) => Task.FromResult(true);
    }
    private sealed class AskTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token) => action(token);
    }
    private sealed class AskQuery(IQueryExecutor inner, AiChatSessionRecord session, Func<AiKnowledgeIndexStatusRecord[]> rows) : IQueryExecutor
    {
        internal Guid Generation;
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken token = default)
        {
            if (typeof(T) == typeof(AiChatSessionRecord)) return Task.FromResult((T?)(object)session);
            if (typeof(T) == typeof(Guid)) { Generation = (Guid)((IReadOnlyDictionary<string, object?>)parameters!)["MessageId"]!; return Task.FromResult((T?)(object)Generation); }
            return inner.QuerySingleOrDefaultAsync<T>(sql, parameters, token);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken token = default)
            => typeof(T) == typeof(AiKnowledgeIndexStatusRecord) ? Task.FromResult((IReadOnlyList<T>)(object)rows()) : inner.QueryAsync<T>(sql, parameters, token);
    }
}
