using NSubstitute;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiAgentRunManagementTests
{
    [TestMethod]
    public void Workflow_creation_and_hash_use_structured_validation_version()
    {
        var request = new CreateAiAgentRunRequest(Guid.CreateVersion7(),
            AiAgentRunManagementService.ChatRenameWorkflowDefinitionKey, Guid.CreateVersion7(), "", 100, 100);
        Assert.AreEqual(2, AiAgentRunManagementService.ResolveDefinitionVersion(request.DefinitionKey));
        Assert.AreEqual(1, AiAgentRunManagementService.ResolveDefinitionVersion(AiAgentRunManagementService.SingleTextDefinitionKey));
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"host|{request.ClientRequestId:N}|{request.DefinitionKey}|2|{request.ModelConfigId:N}|100|100|0:")));
        Assert.AreEqual(expected, InvokeHash(request, "host"));
    }

    [TestMethod]
    public void Request_hash_changes_when_prompt_changes()
    {
        var scope = "host";
        var baseRequest = new CreateAiAgentRunRequest(
            Guid.CreateVersion7(),
            AiAgentRunManagementService.SingleTextDefinitionKey,
            Guid.CreateVersion7(),
            "hello",
            100,
            100);
        var other = baseRequest with { Prompt = "world" };
        var hash1 = InvokeHash(baseRequest, scope);
        var hash2 = InvokeHash(other, scope);
        Assert.AreNotEqual(hash1, hash2);
    }

    private static string InvokeHash(CreateAiAgentRunRequest request, string scope)
    {
        var method = typeof(AiAgentRunManagementService).GetMethod(
            "ComputeRequestHash",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (string)method!.Invoke(null, [request, scope])!;
    }

    [TestMethod]
    public void Exact_prompt_version_changes_request_hash_and_mixed_replay_is_rejected()
    {
        var request = new CreateAiAgentRunRequest(Guid.CreateVersion7(), AiAgentRunManagementService.SingleTextDefinitionKey, Guid.CreateVersion7(), "raw", 100, 100, Guid.CreateVersion7());
        Assert.AreNotEqual(InvokeHash(request, "host"), InvokeHash(request with { PromptVersionId = Guid.CreateVersion7() }, "host"));
        var saved = new Full.NET.Modules.Ai.Runtime.AgentRunBudgetSnapshot(request.ModelConfigId, request.Prompt, 100, 100,
            new(Guid.CreateVersion7(), request.PromptVersionId!.Value, 1, new string('a', 64), 0));
        var row = new Full.NET.Modules.Ai.Persistence.AiAgentRunRecord { DefinitionKey = request.DefinitionKey, BudgetJson = System.Text.Json.JsonSerializer.Serialize(saved, Full.NET.Modules.Ai.Serialization.AiJsonSerializerContext.Default.AgentRunBudgetSnapshot) };
        var method = typeof(AiAgentRunManagementService).GetMethod("ReplayMatches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.IsTrue((bool)method.Invoke(null, [row, request])!);
        Assert.IsFalse((bool)method.Invoke(null, [row, request with { PromptVersionId = null }])!);
        Assert.IsFalse((bool)method.Invoke(null, [row, request with { Prompt = "different" }])!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Creation_race_loser_settles_known_zero_usage_before_replay_or_conflict(bool conflicting)
    {
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var transaction = Substitute.For<ICommandTransaction>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>();
        var loser = Guid.CreateVersion7(); var winner = Guid.CreateVersion7(); ids.NewId().Returns(loser, Guid.CreateVersion7());
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true);
        var database = Options.Create(new DatabaseOptions());
        var runtime = Options.Create(new AiAgentRuntimeOptions { AcceptsNewRuns = true });
        var request = new CreateAiAgentRunRequest(Guid.CreateVersion7(), AiAgentRunManagementService.SingleTextDefinitionKey, Guid.CreateVersion7(), "input", 100, 100);
        var binding = new SessionBindingSnapshot(Guid.CreateVersion7(), null, Guid.CreateVersion7(), "stamp", "host", "host");
        // 首次查询尚无获胜者，进入 CreateOrGet 时另一请求已插入；无需随机调度即可命中落败分支。
        queries.QuerySingleOrDefaultAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Guid.Empty, winner);
        queries.QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        queries.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiModelConfigRecord { Id = request.ModelConfigId, IsEnabled = true, ProviderKey = "ollama", ModelId = "model" });
        var saved = new AgentRunBudgetSnapshot(request.ModelConfigId, request.Prompt, 100, 100,
            conflicting ? new(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, new string('a', 64), 0) : null);
        queries.QuerySingleOrDefaultAsync<AiAgentRunRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiAgentRunRecord { Id = winner, DefinitionKey = request.DefinitionKey, BudgetJson = System.Text.Json.JsonSerializer.Serialize(saved, Full.NET.Modules.Ai.Serialization.AiJsonSerializerContext.Default.AgentRunBudgetSnapshot) });
        var store = new AiAgentRunStore(queries, commands, transaction, ids, clock, database);
        var heartbeat = new AiAgentWorkerHeartbeatService(Substitute.For<IServiceScopeFactory>(), clock, database, runtime);
        var budget = Substitute.For<IAiOperationBudgetStore>();
        var service = new AiAgentRunManagementService(store, new(queries, clock, runtime, heartbeat), null!, new(queries, database), null!, null!, budget, tenant, clock, ids, runtime, transaction);
        var result = await service.CreateAsync(request, binding);
        Assert.AreEqual(!conflicting, result.IsSuccess);
        if (conflicting) Assert.AreEqual("ai.prompts.conflict", result.Error!.Code);
        else Assert.AreEqual(winner, result.Value!.RunId);
        await budget.Received(1).ReserveAsync(Arg.Is<AiOperationRequest>(r => r != null && r.OperationId == loser), Arg.Any<CancellationToken>());
        await budget.Received(1).SettleAsync(loser, Arg.Is<AiOperationUsage>(u => u != null && u.InputTokens == 0 && u.OutputTokens == 0), "cancelled", Arg.Any<CancellationToken>());
        await budget.DidNotReceive().SettleAsync(winner, Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await commands.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

}
