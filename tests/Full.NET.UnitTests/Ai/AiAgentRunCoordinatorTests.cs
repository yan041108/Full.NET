using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Mcp;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Full.NET.Agents.Workflows;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>Worker 协调器在会话失效、模型失败与租约丢失时保持预算与状态边界。</summary>
[TestClass]
public sealed class AiAgentRunCoordinatorTests
{
    [TestMethod]
    public async Task Invalid_session_marks_authorization_required_and_cancels_budget_async()
    {
        var fixture = await CreateFixtureAsync(bindingValid: false).ConfigureAwait(false);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync().ConfigureAwait(false));
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "cancelled", Arg.Any<CancellationToken>());
        AssertStatusCommit(fixture.Commands, "authorization_required");
    }

    [TestMethod]
    public async Task Missing_model_marks_failed_and_settles_budget_async()
    {
        var fixture = await CreateFixtureAsync(includeModel: false).ConfigureAwait(false);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync().ConfigureAwait(false));
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "failed", Arg.Any<CancellationToken>());
        AssertStatusCommit(fixture.Commands, "failed");
    }

    [TestMethod]
    public async Task Runner_failure_marks_failed_and_settles_budget_async()
    {
        var fixture = await CreateFixtureAsync(runnerThrows: true).ConfigureAwait(false);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync().ConfigureAwait(false));
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "failed", Arg.Any<CancellationToken>());
        AssertStatusCommit(fixture.Commands, "failed");
    }

    [TestMethod]
    public async Task Lost_lease_on_commit_skips_budget_settlement_async()
    {
        var fixture = await CreateFixtureAsync(commitSucceeds: false).ConfigureAwait(false);
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync().ConfigureAwait(false));
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Successful_run_commits_progress_and_settles_budget_async()
    {
        var fixture = await CreateFixtureAsync().ConfigureAwait(false);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync().ConfigureAwait(false));
        AssertStatusCommit(fixture.Commands, "completed");
        var settle = fixture.Budget.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IAiOperationBudgetStore.SettleAsync));
        Assert.AreEqual(fixture.RunId, settle.GetArguments()[0]);
        Assert.AreEqual("succeeded", settle.GetArguments()[2]);
        var usage = Assert.IsInstanceOfType<AiOperationUsage>(settle.GetArguments()[1]);
        Assert.AreEqual(2L, usage.InputTokens);
        Assert.AreEqual(1L, usage.OutputTokens);
    }

    [TestMethod]
    public async Task Tool_loop_definition_completes_via_chat_client_and_executor_async()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "ai.tools.ping", new Dictionary<string, object?>())])),
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "tool-loop-done")));
        var fixture = await CreateFixtureAsync(
            definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            chatClient: client).ConfigureAwait(false);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync().ConfigureAwait(false));
        AssertStatusCommit(fixture.Commands, "completed");
        await fixture.ToolExecutor.Received(1).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Tool_loop_approval_required_marks_awaiting_approval_without_budget_settlement_async()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call", "ai.tools.ping", new Dictionary<string, object?>())])),
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "waiting")));
        var toolExecutor = Substitute.For<IAgentToolExecutor>();
        toolExecutor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("denied", null, "ai.tool.approval_required"));
        var fixture = await CreateFixtureAsync(
            definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            chatClient: client,
            toolExecutor: toolExecutor).ConfigureAwait(false);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync().ConfigureAwait(false));
        AssertStatusCommit(fixture.Commands, "awaiting_approval");
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Tool_loop_reconciliation_required_marks_reconciliation_without_budget_settlement_async()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call", "ai.tools.ping", new Dictionary<string, object?>())])),
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "reconcile")));
        var toolExecutor = Substitute.For<IAgentToolExecutor>();
        toolExecutor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("failed", null, "ai.tool.reconciliation_required"));
        var fixture = await CreateFixtureAsync(
            definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            chatClient: client,
            toolExecutor: toolExecutor).ConfigureAwait(false);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync().ConfigureAwait(false));
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Workflow_definition_completes_with_checkpoint_and_budget_settlement_async()
    {
        var sessionId = Guid.CreateVersion7();
        var toolExecutor = Substitute.For<IAgentToolExecutor>();
        toolExecutor.ExecuteAsync(
                Arg.Is<ToolInvocation>(invocation => invocation != null && invocation.ToolName == "ai.chat.sessions.list"),
                Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement("[]"), null));
        toolExecutor.ExecuteAsync(
                Arg.Is<ToolInvocation>(invocation => invocation != null && invocation.ToolName == "ai.chat.sessions.rename"),
                Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(new { ok = true }), null));
        var runner = Substitute.For<IAgentModelRunner>();
        using var session = JsonDocument.Parse("""{"messages":[]}""");
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new AgentModelResult(
                call.ArgAt<string>(1).Contains("session list", StringComparison.OrdinalIgnoreCase) ? "New Title" : "VALID",
                session.RootElement.Clone(),
                2,
                1));
        var fixture = await CreateFixtureAsync(
            definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            toolExecutor: toolExecutor,
            runner: runner,
            sessionId: sessionId).ConfigureAwait(false);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync().ConfigureAwait(false));
        AssertStatusCommit(fixture.Commands, "completed");
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "succeeded", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Tool_loop_budget_exceeded_marks_failed_and_settles_budget_async()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("loop", "ai.tools.ping", new Dictionary<string, object?>())])));
        var fixture = await CreateFixtureAsync(
            definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            chatClient: client).ConfigureAwait(false);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync().ConfigureAwait(false));
        AssertStatusCommit(fixture.Commands, "failed");
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "failed", Arg.Any<CancellationToken>());
    }

    private static async Task<CoordinatorFixture> CreateFixtureAsync(
        bool bindingValid = true,
        bool includeModel = true,
        bool runnerThrows = false,
        bool commitSucceeds = true,
        string definitionKey = AiAgentRunManagementService.SingleTextDefinitionKey,
        IChatClient? chatClient = null,
        IAgentToolExecutor? toolExecutor = null,
        IAgentModelRunner? runner = null,
        Guid? sessionId = null)
    {
        var runId = Guid.CreateVersion7();
        var modelId = Guid.CreateVersion7();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SqlStatement>() == AiAgentRunSql.UpdateRunStatus && !commitSucceeds ? 0 : 1);
        var queries = Substitute.For<IQueryExecutor>();
        queries.QueryAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns([runId]);
        queries.QuerySingleOrDefaultAsync<AgentCheckpointRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((AgentCheckpointRecord?)null);
        var record = CreateRecord(runId, modelId, definitionKey, sessionId);
        queries.QuerySingleOrDefaultAsync<AiAgentRunRecord>(AiAgentRunSql.SelectLease, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(record);
        queries.QuerySingleOrDefaultAsync<AiAgentRunRecord>(AiAgentRunSql.FindById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(record);
        queries.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(includeModel
                ? new AiModelConfigRecord
                {
                    Id = modelId,
                    Version = 1,
                    IsEnabled = true,
                    ProviderKey = "ollama",
                    ModelId = "model",
                    EndpointBaseUrl = "https://provider.test",
                }
                : null);
        var transaction = new DapperCommandTransaction(new RecordingDbTransactionCoordinator());
        var store = new AiAgentRunStore(
            queries,
            commands,
            transaction,
            ids,
            clock,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        var heartbeat = CreateHeartbeat(commands, clock);
        var binding = Substitute.For<IBackgroundSessionBindingValidator>();
        binding.IsValidAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<CancellationToken>())
            .Returns(bindingValid);
        var budget = Substitute.For<IAiOperationBudgetStore>();
        var resolvedRunner = runner ?? Substitute.For<IAgentModelRunner>();
        if (runner is null)
        {
            if (runnerThrows)
            {
                resolvedRunner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns<Task<AgentModelResult>>(_ => throw new InvalidOperationException("model failed"));
            }
            else
            {
                using var session = JsonDocument.Parse("""{"messages":["hello","reply"]}""");
                resolvedRunner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(new AgentModelResult("reply", session.RootElement.Clone(), 2, 1));
            }
        }

        var client = chatClient ?? Substitute.For<IChatClient>();
        var resolvedToolExecutor = toolExecutor ?? CreateDefaultToolExecutor();
        var coordinatorScope = BuildCoordinatorScope(binding, queries, budget, client, ids, resolvedToolExecutor, definitionKey);
        var scopeFactory = coordinatorScope.GetRequiredService<IServiceScopeFactory>();
        var coordinator = new AiAgentRunCoordinator(
            scopeFactory,
            store,
            heartbeat,
            resolvedRunner,
            clock,
            ids,
            Options.Create(new AiAgentRuntimeOptions { BatchSize = 4, LeaseSeconds = 30 }),
            NullLogger<AiAgentRunCoordinator>.Instance);
        await heartbeat.UpsertAsync().ConfigureAwait(false);
        return new CoordinatorFixture(runId, commands, budget, coordinator, resolvedRunner, resolvedToolExecutor);
    }

    private static IAgentToolExecutor CreateDefaultToolExecutor()
    {
        var toolExecutor = Substitute.For<IAgentToolExecutor>();
        toolExecutor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null));
        return toolExecutor;
    }

    private static ServiceProvider BuildCoordinatorScope(
        IBackgroundSessionBindingValidator binding,
        IQueryExecutor queries,
        IAiOperationBudgetStore budget,
        IChatClient client,
        IIdGenerator ids,
        IAgentToolExecutor toolExecutor,
        string definitionKey)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenantContextWriter>(new CurrentTenantAccessor());
        services.AddSingleton(binding);
        services.AddSingleton(queries);
        services.AddSingleton(budget);
        services.AddSingleton(ids);
        services.AddSingleton(toolExecutor);
        services.AddSingleton<IAgentRunExecutionContext, AgentRunExecutionContext>();
        AgentToolDefinition[] tools = definitionKey == AgentWorkflowRegistry.ChatRenameWorkflowKey
            ?
            [
                new AgentToolDefinition("ai.chat.sessions.list", 1, "ai.chat.read", "read", true, null),
                new AgentToolDefinition("ai.chat.sessions.rename", 1, "ai.chat.write", "write", true, null),
            ]
            : [new AgentToolDefinition("ai.tools.ping", 1, "permission", "none", true, null)];
        services.AddSingleton<IAgentToolRegistrySource>(new FixedAgentToolRegistrySource(new AgentToolRegistry(tools)));
        var remoteCatalog = Substitute.For<IMcpRemoteToolCatalog>();
        remoteCatalog.ListExecutableAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<McpRemoteApprovedTool>>([]));
        services.AddSingleton(remoteCatalog);
        services.AddScoped<AiModelBindingScope>();
        var modelFactory = Substitute.For<IAiModelClientFactory>();
        modelFactory.ProviderKey.Returns("ollama");
        modelFactory.CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>())
            .Returns(client);
        services.AddSingleton(modelFactory);
        return services.BuildServiceProvider();
    }

    private static void AssertStatusCommit(ICommandExecutor commands, string statusKey)
    {
        Assert.AreEqual(1, commands.ReceivedCalls().Count(call =>
            call.GetArguments()[0] is SqlStatement statement
            && statement == AiAgentRunSql.UpdateRunStatus
            && call.GetArguments()[1] is IReadOnlyDictionary<string, object?> parameters
            && parameters["StatusKey"] as string == statusKey));
    }

    private static AiAgentWorkerHeartbeatService CreateHeartbeat(ICommandExecutor commands, IClock clock)
    {
        var scope = Substitute.For<IServiceScope>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICurrentTenantContextWriter)).Returns(new CurrentTenantAccessor());
        provider.GetService(typeof(ICommandExecutor)).Returns(commands);
        scope.ServiceProvider.Returns(provider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateAsyncScope().Returns(scope);
        return new AiAgentWorkerHeartbeatService(
            scopeFactory,
            clock,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Options.Create(new AiAgentRuntimeOptions { PollMilliseconds = 1000 }));
    }

    private static AiAgentRunRecord CreateRecord(Guid runId, Guid modelId, string definitionKey, Guid? sessionId = null)
    {
        var budgetJson = JsonSerializer.Serialize(
            new AgentRunBudgetSnapshot(modelId, "hello", 100, 100),
            AiJsonSerializerContext.Default.AgentRunBudgetSnapshot);
        return new AiAgentRunRecord
        {
            Id = runId,
            ScopeKey = "host",
            ActorUserId = Guid.CreateVersion7(),
            SessionId = sessionId ?? Guid.CreateVersion7(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ActorScope = "host-admin",
            EffectiveScope = "host",
            StatusKey = "queued",
            BudgetJson = budgetJson,
            DefinitionKey = definitionKey,
            DefinitionVersion = AiAgentRunManagementService.SingleTextDefinitionVersion,
            Version = 2,
            LeaseEpoch = 1,
            LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
        };
    }

    private sealed record CoordinatorFixture(
        Guid RunId,
        ICommandExecutor Commands,
        IAiOperationBudgetStore Budget,
        AiAgentRunCoordinator Coordinator,
        IAgentModelRunner Runner,
        IAgentToolExecutor ToolExecutor);
}
