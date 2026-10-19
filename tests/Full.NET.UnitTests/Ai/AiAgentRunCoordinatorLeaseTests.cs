using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试分片：AiAgentRunCoordinator。</summary>
public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Stop_waits_for_inflight_renewal_and_only_hands_off_successful_version(bool succeeds)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = await CreateFixtureAsync(configureCommands: commands =>
            commands.ExecuteAsync(AiAgentRunSql.RenewLease, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                entered.TrySetResult();
                return renewal.Task;
            }));
        var lease = new AgentRunLease(fixture.RunId, "worker", 1, 2, DateTimeOffset.UtcNow.AddSeconds(5));
        await using var watch = new AiAgentRunLeaseWatch(fixture.Scopes, fixture.Clock, lease,
            DateTimeOffset.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(1), NullLogger.Instance, CancellationToken.None);
        Task<AgentRunLease>? stopped = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            stopped = watch.StopAsync();
            Assert.IsFalse(stopped.IsCompleted);
            if (succeeds) renewal.TrySetResult(1);
            else renewal.TrySetException(new OperationCanceledException("renewal cancelled independently"));
            var latest = await stopped.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(succeeds, watch.IsActive);
            Assert.AreEqual(succeeds ? lease.Version + 1 : lease.Version, latest.Version);
            Assert.AreEqual(lease.Epoch, latest.Epoch);
        }
        finally { renewal.TrySetResult(1); if (stopped is not null) await stopped; }
    }

    [TestMethod]
    [DataRow("fullnet-single-text-v1")]
    [DataRow("fullnet-readonly-tool-loop-v1")]
    [DataRow("fullnet-chat-rename-workflow-v1")]
    public async Task Long_model_renews_repeatedly_and_commits_latest_lease(string definition)
    {
        var renewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var version = 2L;
        var renewCount = 0;
        var scenario = await CreateLeaseScenarioAsync(definition, configureCommands: commands =>
            commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var statement = call.Arg<SqlStatement>();
                if (statement == AiAgentRunSql.RenewLease || statement == AiAgentRunSql.UpdateRunStatus)
                {
                    var args = call.ArgAt<IReadOnlyDictionary<string, object?>>(1);
                    Assert.AreEqual(version, (long)args["Version"]!);
                    version++;
                    if (statement == AiAgentRunSql.RenewLease)
                    {
                        if (++renewCount >= 2) renewed.TrySetResult();
                    }
                }
                return 1;
            }));
        var pending = scenario.Fixture.Coordinator.ProcessPendingAsync();
        try { await renewed.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { scenario.Release.TrySetResult(); await pending; }
        Assert.AreEqual(1, await pending);
        Assert.IsTrue(renewCount >= 2);
        AssertStatusCommit(scenario.Fixture.Commands, "completed");
        scenario.Client.Received(1).Dispose();
    }

    [TestMethod]
    [DataRow("fullnet-single-text-v1", false, false)]
    [DataRow("fullnet-single-text-v1", true, false)]
    [DataRow("fullnet-single-text-v1", false, true)]
    [DataRow("fullnet-single-text-v1", true, true)]
    [DataRow("fullnet-readonly-tool-loop-v1", false, false)]
    [DataRow("fullnet-readonly-tool-loop-v1", true, false)]
    [DataRow("fullnet-readonly-tool-loop-v1", false, true)]
    [DataRow("fullnet-readonly-tool-loop-v1", true, true)]
    [DataRow("fullnet-chat-rename-workflow-v1", false, false)]
    [DataRow("fullnet-chat-rename-workflow-v1", true, false)]
    [DataRow("fullnet-chat-rename-workflow-v1", false, true)]
    [DataRow("fullnet-chat-rename-workflow-v1", true, true)]
    public async Task Failed_renewal_cancels_dispatch_and_discards_late_model_outcome(string definition, bool renewalThrows, bool modelThrows)
    {
        var scenario = await CreateLeaseScenarioAsync(definition, modelThrows: modelThrows, configureCommands: commands =>
            commands.ExecuteAsync(AiAgentRunSql.RenewLease, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns<int>(_ => renewalThrows ? throw new InvalidOperationException("renewal unavailable") : 0));
        await AssertLeaseCancellationAsync(scenario);
    }

    [TestMethod]
    [DataRow("fullnet-single-text-v1")]
    [DataRow("fullnet-readonly-tool-loop-v1")]
    [DataRow("fullnet-chat-rename-workflow-v1")]
    public async Task Deadline_cancels_noncooperative_model_and_rejects_late_completion(string definition)
    {
        var scenario = await CreateLeaseScenarioAsync(definition, configureRecord: r => r.DeadlineAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(500));
        await AssertLeaseCancellationAsync(scenario);
    }

    [TestMethod]
    [DataRow("fullnet-single-text-v1")]
    [DataRow("fullnet-readonly-tool-loop-v1")]
    [DataRow("fullnet-chat-rename-workflow-v1")]
    public async Task Lease_expiry_cancels_model_even_when_renewal_ignores_token(string definition)
    {
        var renewal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scenario = await CreateLeaseScenarioAsync(definition,
            configureRecord: r => r.LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(800),
            configureCommands: commands => commands.ExecuteAsync(AiAgentRunSql.RenewLease, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(renewal.Task));
        try { await AssertLeaseCancellationAsync(scenario, () => renewal.TrySetResult(1)); }
        finally { renewal.TrySetResult(1); }
    }

    [TestMethod]
    [DataRow("fullnet-single-text-v1", true)]
    [DataRow("fullnet-single-text-v1", false)]
    [DataRow("fullnet-readonly-tool-loop-v1", true)]
    [DataRow("fullnet-readonly-tool-loop-v1", false)]
    [DataRow("fullnet-chat-rename-workflow-v1", true)]
    [DataRow("fullnet-chat-rename-workflow-v1", false)]
    public async Task Expired_authority_never_creates_model_client(string definition, bool deadline)
    {
        var fixture = await CreateFixtureAsync(definitionKey: definition, configureRecord: r =>
        {
            if (deadline) r.DeadlineAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
            else r.LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        });
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        AssertNoProgress(fixture);
        Assert.AreEqual(0, fixture.Factory.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "CreateChatClientAsync"));
    }

    private static async Task AssertLeaseCancellationAsync(LeaseScenario scenario, Action? releaseRenewal = null)
    {
        var pending = scenario.Fixture.Coordinator.ProcessPendingAsync();
        var progressBeforeRelease = 0;
        try
        {
            await scenario.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await scenario.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            progressBeforeRelease = scenario.Fixture.Commands.ReceivedCalls().Count(c => c.GetArguments()[0] is SqlStatement s && s == AiAgentRunSql.UpdateRunStatus);
        }
        finally { releaseRenewal?.Invoke(); scenario.Release.TrySetResult(); await pending; }
        Assert.AreEqual(0, await pending);
        // 失租前已完成的节点和当前派发意图必须保留；失租后不得追加状态或结算。
        var beforeRelease = scenario.Fixture.Commands.ReceivedCalls().Where(c => c.GetArguments()[0] is SqlStatement s && s == AiAgentRunSql.UpdateRunStatus).ToArray();
        Assert.AreEqual(progressBeforeRelease, beforeRelease.Length);
        Assert.IsTrue(beforeRelease.All(c => ((IReadOnlyDictionary<string, object?>)c.GetArguments()[1]!)["StatusKey"]?.ToString() == "running"));
        Assert.AreEqual(0, scenario.Fixture.Budget.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "SettleAsync"));
        Assert.AreEqual(1, scenario.Client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "GetResponseAsync"));
        scenario.Client.Received(1).Dispose();
    }

    private static void AssertNoProgress(CoordinatorFixture fixture)
    {
        Assert.AreEqual(0, fixture.Commands.ReceivedCalls().Count(c => c.GetArguments()[0] is SqlStatement s && s == AiAgentRunSql.UpdateRunStatus));
        Assert.AreEqual(0, fixture.Budget.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "SettleAsync"));
    }

    private static async Task<LeaseScenario> CreateLeaseScenarioAsync(string definition, bool modelThrows = false,
        Action<AiAgentRunRecord>? configureRecord = null, Action<ICommandExecutor>? configureCommands = null)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            using var registration = call.ArgAt<CancellationToken>(2).Register(() => cancelled.TrySetResult());
            entered.TrySetResult();
            // 故意忽略令牌，使晚到成功和晚到异常都经过真实协调器边界。
            await release.Task;
            if (modelThrows) throw new InvalidOperationException("late provider failure");
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
                { Usage = new() { InputTokenCount = 2, OutputTokenCount = 1 } };
        });
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await call.ArgAt<IChatClient>(0).GetResponseAsync([new(ChatRole.User, "prompt")], cancellationToken: call.ArgAt<CancellationToken>(2));
            var text = call.ArgAt<string>(1).Contains("session list", StringComparison.OrdinalIgnoreCase) ? "New Title" : """{"decision":"approved"}""";
            return new AgentModelResult(text, JsonSerializer.SerializeToElement(new { messages = Array.Empty<string>() }), 2, 1);
        });
        var fixture = await CreateFixtureAsync(definitionKey: definition, chatClient: client, runner: runner, leaseSeconds: 1,
            configureRecord: configureRecord, configureCommands: configureCommands);
        return new(fixture, client, entered, cancelled, release);
    }

    private sealed record LeaseScenario(CoordinatorFixture Fixture, IChatClient Client, TaskCompletionSource Entered,
        TaskCompletionSource Cancelled, TaskCompletionSource Release);
}
