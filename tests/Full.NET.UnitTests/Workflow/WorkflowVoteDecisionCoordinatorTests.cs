using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>投票只有完整参与或可信截止才可写最终事实，部分票达到比例也不能提前关闭。</summary>
[TestClass]
public sealed class WorkflowVoteDecisionCoordinatorTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, 2, 0, 0, 1, false, "Waiting")]
    [DataRow(DatabaseProvider.MySql, 2, 0, 0, 1, false, "Waiting")]
    [DataRow(DatabaseProvider.SqlServer, 3, 0, 0, 0, false, "Approved")]
    [DataRow(DatabaseProvider.MySql, 2, 1, 0, 0, false, "Rejected")]
    [DataRow(DatabaseProvider.SqlServer, 0, 0, 3, 0, false, "Suspended")]
    [DataRow(DatabaseProvider.MySql, 1, 0, 0, 2, true, "Rejected")]
    public async Task Only_authoritative_final_outcomes_cancel_remaining_candidates(DatabaseProvider provider,
        int yes, int no, int abstain, int pending, bool deadline, string expected)
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QuerySingleOrDefaultAsync<WorkflowVoteCountsRecord>(provider == DatabaseProvider.SqlServer ? WorkflowVoteSql.TallySqlServer : WorkflowVoteSql.TallyMySql,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowVoteCountsRecord(yes, no, abstain, pending, 0));
        var now = DateTimeOffset.UtcNow;
        var policy = new WorkflowVotePolicy(Guid.CreateVersion7(), 66, 100, true, 60, null, null);
        var round = new WorkflowVoteRoundRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 3, 66, 100, true,
            now.AddSeconds(deadline ? -1 : 60), "active", 1, null, null, null, null);
        var result = await new WorkflowVoteDecisionCoordinator(query, command, Options.Create(new DatabaseOptions { Provider = provider }))
            .ResolveAsync(round, policy, new(null, "host", "host"), now, deadline, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(expected, result.Value!.Outcome.ToString());
        if (expected == "Waiting") Assert.AreEqual(0, command.ReceivedCalls().Count());
        else
        {
            await command.Received(1).ExecuteAsync(WorkflowVoteSql.CloseRound, Arg.Any<object?>(), Arg.Any<CancellationToken>());
            await command.Received(1).ExecuteAsync(WorkflowVoteSql.CancelPendingBallots, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        }
        await command.DidNotReceive().ExecuteAsync(WorkflowSql.DecideApprovalSlotWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("future")]
    [DataRow("unconfigured")]
    [DataRow("denominator")]
    [DataRow("cancelled")]
    [DataRow("cas")]
    public async Task Invalid_deadline_snapshot_and_cas_do_not_cancel_candidates(string failure)
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(failure == "cas" ? 0 : 1);
        query.QuerySingleOrDefaultAsync<WorkflowVoteCountsRecord>(WorkflowVoteSql.TallySqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowVoteCountsRecord(3, 0, 0, failure == "denominator" ? 1 : 0, failure == "cancelled" ? 1 : 0));
        var now = DateTimeOffset.UtcNow;
        var policy = new WorkflowVotePolicy(Guid.CreateVersion7(), 66, 100, true, failure == "unconfigured" ? null : 60, null, null);
        var round = new WorkflowVoteRoundRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 3, 66, 100, true,
            failure == "unconfigured" ? null : now.AddSeconds(failure == "future" ? 60 : -1), "active", 1, null, null, null, null);
        var result = await new WorkflowVoteDecisionCoordinator(query, command, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }))
            .ResolveAsync(round, policy, new(null, "host", "host"), now, true, default);
        Assert.IsFalse(result.IsSuccess);
        await command.DidNotReceive().ExecuteAsync(WorkflowVoteSql.CancelPendingTodos, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(WorkflowVoteSql.CancelPendingBallots, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
}
