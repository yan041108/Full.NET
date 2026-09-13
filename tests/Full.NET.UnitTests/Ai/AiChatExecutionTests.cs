using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Extensions;

namespace Full.NET.UnitTests.Ai;

/// <summary>用真实编排与 Provider 覆盖预检、故障收尾和终态通知顺序。</summary>
[TestClass]
public sealed class AiChatExecutionTests
{
    [TestMethod]
    [DataRow("success")]
    [DataRow("host")]
    [DataRow("quota")]
    [DataRow("provider")]
    [DataRow("save")]
    [DataRow("save_zero")]
    [DataRow("settle")]
    [DataRow("release")]
    [DataRow("lost_lease")]
    [DataRow("disconnect")]
    public async Task Generation_preserves_preflight_and_cleanup_boundaries_async(string scenario)
    {
        var tenantContext = new TenantContext(Guid.NewGuid(), "tenant", "租户");
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(tenantContext);
        if (scenario == "host") tenant.SetHost();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var generationId = Guid.NewGuid();
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.NewGuid(), generationId);
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<AiChatSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiChatSessionRecord { Id = Guid.NewGuid(), ModelConfigId = Guid.NewGuid() });
        queries.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiModelConfigRecord { Id = Guid.NewGuid(), IsEnabled = true, ProviderKey = "ollama", ModelId = "model", EndpointBaseUrl = "https://provider.test" });
        queries.QuerySingleOrDefaultAsync<AiTenantQuotaRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiTenantQuotaRecord { IsEnabled = true });
        queries.ReturnsForAll<Task<IReadOnlyList<AiChatMessageRecord>>>(
            Task.FromResult<IReadOnlyList<AiChatMessageRecord>>(Array.Empty<AiChatMessageRecord>()));
        var commands = Substitute.For<ICommandExecutor>();
        var output = new RecordingOutput();
        var released = false;
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var statement = call.Arg<SqlStatement>();
            if (statement == AiQuotaReservationSql.Reserve)
            {
                Assert.IsFalse(output.HasStarted);
                if (scenario == "quota") return 0;
            }
            if (statement == AiChatGenerationSql.Renew && scenario == "lost_lease") return 0;
            if (statement == AiChatSql.UpdateMessage)
            {
                if (scenario == "save") throw new IOException("private database detail");
                if (scenario == "save_zero") return 0;
            }
            if (statement == AiQuotaReservationSql.Settle)
            {
                Assert.IsTrue(call.Arg<CancellationToken>().CanBeCanceled);
                Assert.IsFalse(output.Done);
                if (scenario == "settle") throw new IOException("private settlement detail");
            }
            if (statement == AiChatGenerationSql.Release)
            {
                released = true;
                Assert.IsFalse(output.Done);
                Assert.IsTrue(call.Arg<CancellationToken>().CanBeCanceled);
                Assert.AreEqual(generationId, ((IReadOnlyDictionary<string, object?>)call[1]!)["GenerationId"]);
                if (scenario == "release") throw new IOException("private release detail");
            }
            return 1;
        });
        var transaction = new DapperCommandTransaction(new RecordingDbTransactionCoordinator());
        var resolver = Substitute.For<IActiveTenantContextResolver>();
        resolver.ResolveActiveByIdAsync(tenantContext.Id, Arg.Any<CancellationToken>()).Returns(tenantContext);
        var budget = Substitute.For<IAiOperationBudgetStore>();
        var oldGuard = new AiChatQuotaGuard(queries, commands, transaction, clock);
        AiQuotaReservation? oldReceipt = null;
        budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var request = call.Arg<AiOperationRequest>()!;
            Assert.IsFalse(output.HasStarted);
            var result = await oldGuard.ReserveAsync(request.OperationId, request.InputTokenLimit + request.OutputTokenLimit, call.Arg<CancellationToken>());
            if (!result.IsSuccess) throw new AiBudgetException(result.Error!.Code);
            oldReceipt = result.Value;
            return new AiOperationReservation(request.OperationId, true, "reserved", oldReceipt!.ReservedTokens, null, null);
        });
        budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var usage = call.Arg<AiOperationUsage>()!;
            return oldGuard.SettleAsync(oldReceipt!, (int?)usage.InputTokens, (int?)usage.OutputTokens, call.Arg<CancellationToken>());
        });
        await using var services = new ServiceCollection()
            .AddScoped<ICurrentTenantContextWriter, CurrentTenantAccessor>()
            .AddSingleton(resolver).AddScoped<ICommandExecutor>(_ => commands)
            .AddScoped<IAiOperationBudgetStore>(_ => budget).BuildServiceProvider();
        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        var monitor = new AiChatGenerationLeaseMonitor(scopes, clock, NullLogger<AiChatGenerationLeaseMonitor>.Instance);
        var registry = new AiChatGenerationRegistry();
        var options = Options.Create(new DatabaseOptions());
        var http = Substitute.For<IHttpClientFactory>();
        http.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new ResponseHandler(scenario == "provider")));
        var service = new AiChatStreamService(queries, commands, transaction,
            new AiChatSessionQueryService(queries, tenant, options, clock), TestAiProviders.Streamer(http), registry,
            monitor, budget,
            new AiChatCleanupScope(scopes, NullLogger<AiChatCleanupScope>.Instance), tenant, options, clock, ids);
        using var disconnected = new CancellationTokenSource();
        if (scenario == "disconnect") output.OnDelta = () => disconnected.Cancel();
        var sessionId = Guid.NewGuid();
        var running = service.StreamAsync(sessionId, Guid.NewGuid(), new("hello"), output, disconnected.Token);
        if (scenario == "disconnect") await Assert.ThrowsAsync<OperationCanceledException>(() => running);
        else
        {
            var result = await running;
            if (scenario == "quota")
            {
                Assert.IsFalse(output.HasStarted);
                Assert.AreEqual(AiErrorCodes.TenantQuotaExceeded, result.Error!.Code);
                Assert.AreEqual(403, StandardApiResultMapper.ToStatusCode(result.Error.Type));
                http.DidNotReceive().CreateClient(Arg.Any<string>());
            }
            else if (scenario == "lost_lease")
            {
                Assert.IsFalse(output.HasStarted);
                Assert.IsFalse(result.IsSuccess);
                http.DidNotReceive().CreateClient(Arg.Any<string>());
            }
            else
            {
                Assert.IsTrue(result.IsSuccess, "流启动后的失败通过语义事件交付。");
                Assert.AreEqual(scenario is "success" or "host", output.Done);
                Assert.AreEqual(scenario is not ("success" or "host"), output.Error is not null);
                Assert.DoesNotContain("private", output.Error ?? "", StringComparison.Ordinal);
            }
        }
        if (scenario == "host")
            await commands.Received(1).ExecuteAsync(AiQuotaReservationSql.Reserve, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.IsTrue(released);
        Assert.IsFalse(registry.TryCancel(sessionId, generationId));
    }

    private sealed class ResponseHandler(bool failed) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(failed ? "{\"error\":\"private provider detail\"}\n" :
                    "{\"message\":{\"content\":\"hello\"},\"done\":true,\"prompt_eval_count\":2,\"eval_count\":1}\n")
            });
    }

    private sealed class RecordingOutput : IAiChatOutput
    {
        public bool HasStarted { get; private set; }
        internal bool Done { get; private set; }
        internal string? Error { get; private set; }
        internal Action? OnDelta { get; set; }
        public Task StartAsync(CancellationToken cancellationToken) { HasStarted = true; return Task.CompletedTask; }
        public Task WriteDeltaAsync(string delta, CancellationToken cancellationToken) { OnDelta?.Invoke(); return Task.CompletedTask; }
        public Task WriteDoneAsync(Guid messageId, int? promptTokens, int? completionTokens, CancellationToken cancellationToken)
        { Done = true; return Task.CompletedTask; }
        public Task WriteErrorAsync(string message, CancellationToken cancellationToken) { Error = message; return Task.CompletedTask; }
    }
}

