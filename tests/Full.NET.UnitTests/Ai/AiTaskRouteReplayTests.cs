using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Text.Json;

namespace Full.NET.UnitTests.Ai;

/// <summary>封闭工具路由不能通过既有幂等键返回其他定义、旧版本或不同预算。</summary>
[TestClass]
public sealed class AiTaskRouteReplayTests
{
    [TestMethod]
    [DataRow("definition", false)] [DataRow("definition", true)]
    [DataRow("version", false)] [DataRow("version", true)]
    [DataRow("model", false)] [DataRow("model", true)]
    [DataRow("prompt", false)] [DataRow("prompt", true)]
    [DataRow("input", false)] [DataRow("input", true)]
    [DataRow("output", false)] [DataRow("output", true)]
    [DataRow("malformed", false)] [DataRow("malformed", true)]
    [DataRow("valid", false)] [DataRow("valid", true)]
    public async Task Routed_replay_and_race_require_exact_existing_request(string fault, bool race)
    {
        var queries = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        var transaction = Substitute.For<ICommandTransaction>(); var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>(); var loser = Guid.CreateVersion7(); var winner = Guid.CreateVersion7();
        ids.NewId().Returns(loser, Guid.CreateVersion7());
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true);
        var database = Options.Create(new DatabaseOptions()); var runtime = Options.Create(new AiAgentRuntimeOptions { AcceptsNewRuns = true });
        var request = new CreateAiAgentRunRequest(Guid.CreateVersion7(), AiAgentRunManagementService.ChatRenameWorkflowDefinitionKey,
            Guid.CreateVersion7(), "", 100, 100);
        var binding = new SessionBindingSnapshot(Guid.CreateVersion7(), null, Guid.CreateVersion7(), "stamp", "host", "host");
        queries.QuerySingleOrDefaultAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(race ? Guid.Empty : winner, winner);
        queries.QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        queries.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiModelConfigRecord { Id = request.ModelConfigId, IsEnabled = true, ProviderKey = "ollama", ModelId = "model" });
        var snapshot = new AgentRunBudgetSnapshot(fault == "model" ? Guid.CreateVersion7() : request.ModelConfigId,
            fault == "prompt" ? "other" : request.Prompt, fault == "input" ? 101 : 100, fault == "output" ? 101 : 100);
        var row = new AiAgentRunRecord { Id = winner,
            DefinitionKey = fault == "definition" ? AiAgentRunManagementService.ReadOnlyToolLoopDefinitionKey : request.DefinitionKey,
            DefinitionVersion = fault == "version" ? 1 : 2,
            BudgetJson = fault == "malformed" ? "{" : JsonSerializer.Serialize(snapshot, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot) };
        queries.QuerySingleOrDefaultAsync<AiAgentRunRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        var store = new AiAgentRunStore(queries, commands, transaction, ids, clock, database);
        var heartbeat = new AiAgentWorkerHeartbeatService(Substitute.For<IServiceScopeFactory>(), clock, database, runtime);
        var budget = Substitute.For<IAiOperationBudgetStore>();
        var service = new AiAgentRunManagementService(store, new(queries, clock, runtime, heartbeat), null!, new(queries, database),
            null!, null!, budget, tenant, clock, ids, runtime, transaction);
        var result = await service.CreateRoutedAsync(request, binding);
        Assert.AreEqual(fault == "valid", result.IsSuccess);
        if (result.IsSuccess) Assert.AreEqual(winner, result.Value!.RunId);
        else Assert.AreEqual("ai.routing.conflict", result.Error!.Code);
        if (race)
        {
            await budget.Received(1).ReserveAsync(Arg.Is<AiOperationRequest>(r => r != null && r.OperationId == loser), Arg.Any<CancellationToken>());
            await budget.Received(1).SettleAsync(loser, Arg.Is<AiOperationUsage>(u => u != null && u.InputTokens == 0 && u.OutputTokens == 0), "cancelled", Arg.Any<CancellationToken>());
        }
        else Assert.AreEqual(0, budget.ReceivedCalls().Count());
        await commands.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
}
