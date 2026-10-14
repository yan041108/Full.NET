using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>日志故障不能跳过运行对账或使完成事务失败释放未知预算。</summary>
public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow(false, false)] [DataRow(false, true)]
    [DataRow(true, false)] [DataRow(true, true)]
    public async Task Failure_log_coordinator_preserves_reconciliation_and_budget_when_logger_fails(bool completionFails, bool loggerThrows)
    {
        var logger = new FailureCaptureLogger<AiAgentRunCoordinator> { ThrowOnLog = loggerThrows };
        var fixture = await CreateFixtureAsync(runnerThrows: !completionFails, logger: logger, configureCommands: command =>
        {
            if (completionFails) command.ExecuteAsync(AiAgentRunSql.UpdateRunStatus, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromException<int>(new InvalidOperationException("secret-transaction-payload")));
        });
        Assert.AreEqual(completionFails ? 0 : 1, await fixture.Coordinator.ProcessPendingAsync());
        if (!completionFails) AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.IsTrue(logger.Entries.Count > 0);
        foreach (var entry in logger.Entries)
        {
            Assert.IsNull(entry.Exception);
            Assert.AreEqual(fixture.RunId, entry.State["OperationId"]);
            Assert.IsFalse(entry.Text.Contains("secret-transaction-payload", StringComparison.Ordinal));
        }
    }
}
