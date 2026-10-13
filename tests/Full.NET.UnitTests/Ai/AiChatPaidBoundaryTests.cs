using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Extensions;

namespace Full.NET.UnitTests.Ai;

/// <summary>以真实生成/续租/独立清理编排核对原预算身份、派发与零/未知/完整回执。</summary>
[TestClass]
public sealed class AiChatPaidBoundaryTests
{
    [TestMethod] [DataRow(false, "success")] [DataRow(false, "duplicate")] [DataRow(false, "identity")] [DataRow(false, "state")] [DataRow(false, "factory")] [DataRow(false, "null_factory")] [DataRow(false, "factory_cancel")] [DataRow(false, "start")] [DataRow(false, "callback")] [DataRow(false, "provider")] [DataRow(false, "unknown")] [DataRow(false, "disposed")] [DataRow(false, "disposed_cancel")] [DataRow(false, "null_source")] [DataRow(true, "success")] [DataRow(true, "duplicate")] [DataRow(true, "identity")] [DataRow(true, "state")] [DataRow(true, "factory")] [DataRow(true, "null_factory")] [DataRow(true, "factory_cancel")] [DataRow(true, "start")] [DataRow(true, "callback")] [DataRow(true, "provider")] [DataRow(true, "unknown")] [DataRow(true, "disposed")] [DataRow(true, "disposed_cancel")] [DataRow(true, "null_source")]
    public async Task Paid_receipt_chat_preserves_scope_and_charge_ownership(bool scoped, string fault)
    {
        using var cancellation = new CancellationTokenSource(); var tenant = new CurrentTenantAccessor();
        var tenantContext = new TenantContext(Guid.CreateVersion7(), "tenant", "租户"); if (scoped) tenant.SetTenant(tenantContext); else tenant.SetHost();
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var generation = Guid.CreateVersion7(); var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Guid.CreateVersion7(), generation);
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var model = PaidChatFactory.Model(); var owner = Guid.CreateVersion7(); var session = Guid.CreateVersion7();
        query.QuerySingleOrDefaultAsync<AiChatSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new AiChatSessionRecord { Id = session, OwnerUserId = owner, ModelConfigId = model.Id });
        query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(model);
        query.ReturnsForAll<Task<IReadOnlyList<AiChatMessageRecord>>>(Task.FromResult<IReadOnlyList<AiChatMessageRecord>>([new() { RoleKey = "user", Content = "问题" }]));
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var transaction = new DapperCommandTransaction(new RecordingDbTransactionCoordinator()); var budget = Substitute.For<IAiOperationBudgetStore>();
        budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            new AiOperationReservation(fault == "identity" ? Guid.CreateVersion7() : call.Arg<AiOperationRequest>()!.OperationId, fault != "duplicate", fault == "state" ? "known" : "reserved", 100, null, null));
        var resolver = Substitute.For<IActiveTenantContextResolver>(); resolver.ResolveActiveByIdAsync(tenantContext.Id, Arg.Any<CancellationToken>()).Returns(tenantContext);
        await using var services = new ServiceCollection().AddScoped<ICurrentTenantContextWriter, CurrentTenantAccessor>().AddSingleton(resolver)
            .AddScoped<ICommandExecutor>(_ => command).AddScoped<IAiOperationBudgetStore>(_ => budget).BuildServiceProvider();
        var scopes = services.GetRequiredService<IServiceScopeFactory>(); var options = Options.Create(new DatabaseOptions());
        var factory = new PaidChatFactory(fault, cancellation); using var bindings = new AiModelBindingScope();
        var registry = new AiChatGenerationRegistry(); var output = new Output(fault);
        var service = new AiChatStreamService(query, command, transaction, new(query, tenant, options, clock), new([factory], bindings), registry,
            new(scopes, clock, NullLogger<AiChatGenerationLeaseMonitor>.Instance), budget, new(scopes, NullLogger<AiChatCleanupScope>.Instance), tenant, options, clock, ids);
        if (fault is "factory_cancel" or "disposed_cancel") await Assert.ThrowsAsync<OperationCanceledException>(() => service.StreamAsync(session, owner, new("问题"), output, cancellation.Token));
        else await service.StreamAsync(session, owner, new("问题"), output, cancellation.Token);
        var rejected = fault is "duplicate" or "identity" or "state";
        if (rejected)
        { Assert.IsFalse(output.HasStarted); Assert.AreEqual(0, factory.Creates); await budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()); }
        else
        {
            var usage = fault is "factory" or "null_factory" or "factory_cancel" or "start" ? new AiOperationUsage(0, 0)
                : fault is "success" or "disposed" or "disposed_cancel" ? new AiOperationUsage(7, 3, 2) : new AiOperationUsage(null, null);
            await budget.Received(1).SettleAsync(generation, usage, Arg.Any<string>(), Arg.Any<CancellationToken>());
            if (fault is "factory_cancel" or "start" or "factory" or "null_factory") Assert.AreEqual(0, factory.Calls);
        }
        Assert.IsFalse(registry.TryCancel(session, generation));
        await command.Received(1).ExecuteAsync(AiChatGenerationSql.Release, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(scoped ? tenantContext.Id : (Guid?)null, tenant.Id);
    }
    private sealed class Output(string fault) : IAiChatOutput
    {
        public bool HasStarted { get; private set; }
        public Task StartAsync(CancellationToken token) { if (fault == "start") throw new IOException("启动失败"); HasStarted = true; return Task.CompletedTask; }
        public Task WriteDeltaAsync(string text, CancellationToken token) { if (fault == "callback") throw new IOException("连接断开"); return Task.CompletedTask; }
        public Task WriteErrorAsync(string message, CancellationToken token) => Task.CompletedTask;
        public Task WriteDoneAsync(Guid messageId, int? promptTokens, int? completionTokens, CancellationToken token) => Task.CompletedTask;
    }
}
