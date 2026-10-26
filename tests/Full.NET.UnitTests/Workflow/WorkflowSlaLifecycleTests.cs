using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>截止暂停与延时器相互独立，修订失败必须回滚整个生命周期事务。</summary>
[TestClass]
public sealed class WorkflowSlaLifecycleTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Freeze_preserves_remaining_and_resume_uses_original_amount(DatabaseProvider provider)
    {
        var now = DateTimeOffset.Parse("2026-10-26T00:00:00Z"); var row = Row(now.AddMinutes(10));
        var commands = Commands(1);
        Assert.IsTrue(await WorkflowSlaLifecycle.ApplyAsync(row.InstanceId, "host", "pause", now, provider, Query([row]), commands, default));
        Assert.AreEqual(600000L, Parameters(commands)["RemainingMilliseconds"]);
        row = row with { PausedAtUtc = now, RemainingMilliseconds = 600000L };
        commands = Commands(1);
        Assert.IsTrue(await WorkflowSlaLifecycle.ApplyAsync(row.InstanceId, "host", "resume", now.AddDays(1), provider, Query([row]), commands, default));
        Assert.AreEqual(now.AddDays(1).AddMinutes(10), Parameters(commands)["DueAtUtc"]);
    }

    [TestMethod]
    public async Task Elapsed_policy_and_never_paused_resume_do_not_move_deadline()
    {
        var row = Row(DateTimeOffset.UtcNow); var commands = Commands(1);
        Assert.IsTrue(await WorkflowSlaLifecycle.ApplyAsync(row.InstanceId, "host", "pause", DateTimeOffset.UtcNow, DatabaseProvider.MySql,
            Query([row with { PauseModeKey = "elapsed" }]), commands, default));
        Assert.IsTrue(await WorkflowSlaLifecycle.ApplyAsync(row.InstanceId, "host", "resume", DateTimeOffset.UtcNow, DatabaseProvider.MySql, Query([row]), commands, default));
        Assert.AreEqual(0, commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Corrupt_scope_remaining_or_lost_revision_rejects_control()
    {
        var now = DateTimeOffset.UtcNow; var row = Row(now);
        foreach (var invalid in new[] { row with { TenantScopeKey = "foreign" }, row with { InstanceId = Guid.CreateVersion7() }, row with { PauseModeKey = "implicit" },
                     row with { PausedAtUtc = now, RemainingMilliseconds = -1 } })
        {
            var commands = Commands(1);
            Assert.IsFalse(await WorkflowSlaLifecycle.ApplyAsync(row.InstanceId, "host", "resume", now, DatabaseProvider.SqlServer, Query([invalid]), commands, default));
            Assert.AreEqual(0, commands.ReceivedCalls().Count());
        }
        Assert.IsFalse(await WorkflowSlaLifecycle.ApplyAsync(row.InstanceId, "host", "pause", now, DatabaseProvider.SqlServer, Query([row]), Commands(0), default));
        var bounded = Commands(1);
        Assert.IsFalse(await WorkflowSlaLifecycle.ApplyAsync(row.InstanceId, "host", "pause", now, DatabaseProvider.SqlServer, Query(Enumerable.Repeat(row, 129).ToArray()), bounded, default));
        Assert.AreEqual(0, bounded.ReceivedCalls().Count());
    }
    private static WorkflowSlaRecord Row(DateTimeOffset due) => new(Guid.CreateVersion7(), Guid.CreateVersion7(), "host", "suspend", "freeze", due,
        null, null, "waiting", 3, due.AddMinutes(-10), null, "approval", "human.approval", 2, null, null, "active");
    private static IQueryExecutor Query(IReadOnlyList<WorkflowSlaRecord> rows)
    { var query = Substitute.For<IQueryExecutor>(); query.QueryAsync<WorkflowSlaRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(rows)); return query; }
    private static ICommandExecutor Commands(int affected)
    { var commands = Substitute.For<ICommandExecutor>(); commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(affected)); return commands; }
    private static IReadOnlyDictionary<string, object?> Parameters(ICommandExecutor commands) => (IReadOnlyDictionary<string, object?>)commands.ReceivedCalls().Single().GetArguments()[1]!;
}
