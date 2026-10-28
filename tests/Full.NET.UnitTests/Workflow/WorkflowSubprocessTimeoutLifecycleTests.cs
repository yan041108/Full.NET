using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowSubprocessTimeoutLifecycleTests
{
    [TestMethod]
    public async Task Freeze_and_resume_preserve_remaining_duration()
    {
        var now = DateTimeOffset.UtcNow; var row = Row(now.AddMinutes(10)); var commands = Commands(1);
        Assert.IsTrue(await WorkflowSubprocessTimeoutLifecycle.ApplyAsync(row.InstanceId, "host", "pause", now, Query([row]), commands, default));
        Assert.AreEqual(600000L, Parameters(commands)["RemainingMilliseconds"]);
        commands = Commands(1); row = row with { PausedAtUtc = now, RemainingMilliseconds = 600000L };
        Assert.IsTrue(await WorkflowSubprocessTimeoutLifecycle.ApplyAsync(row.InstanceId, "host", "resume", now.AddDays(1), Query([row]), commands, default));
        Assert.AreEqual(now.AddDays(1).AddMinutes(10), Parameters(commands)["DueAtUtc"]);
    }
    [TestMethod]
    public async Task Elapsed_and_unpaused_resume_do_not_write()
    {
        var now = DateTimeOffset.UtcNow; var row = Row(now); var commands = Commands(1);
        Assert.IsTrue(await WorkflowSubprocessTimeoutLifecycle.ApplyAsync(row.InstanceId, "host", "pause", now, Query([row with { PauseModeKey = "elapsed" }]), commands, default));
        Assert.IsTrue(await WorkflowSubprocessTimeoutLifecycle.ApplyAsync(row.InstanceId, "host", "resume", now, Query([row]), commands, default));
        Assert.AreEqual(0, commands.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Scope_corruption_invalid_remaining_and_lost_cas_reject_control()
    {
        var now = DateTimeOffset.UtcNow; var row = Row(now);
        foreach (var invalid in new[] { row with { TenantScopeKey = "foreign" }, row with { InstanceId = Guid.CreateVersion7() }, row with { StatusKey = "closing" },
                     row with { PauseModeKey = "implicit" }, row with { PausedAtUtc = now, RemainingMilliseconds = -1 }, row with { Revision = 0 } })
        {
            var commands = Commands(1);
            Assert.IsFalse(await WorkflowSubprocessTimeoutLifecycle.ApplyAsync(row.InstanceId, "host", "resume", now, Query([invalid]), commands, default));
            Assert.AreEqual(0, commands.ReceivedCalls().Count());
        }
        Assert.IsFalse(await WorkflowSubprocessTimeoutLifecycle.ApplyAsync(row.InstanceId, "host", "pause", now, Query([row]), Commands(0), default));
    }
    private static WorkflowSubprocessTimeoutRecord Row(DateTimeOffset due) => new(Guid.CreateVersion7(), Guid.CreateVersion7(), "host", due, "freeze", null, null, "waiting", 1);
    private static IQueryExecutor Query(IReadOnlyList<WorkflowSubprocessTimeoutRecord> rows)
    { var query = Substitute.For<IQueryExecutor>(); query.QueryAsync<WorkflowSubprocessTimeoutRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(rows)); return query; }
    private static ICommandExecutor Commands(int count)
    { var commands = Substitute.For<ICommandExecutor>(); commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(count)); return commands; }
    private static IReadOnlyDictionary<string, object?> Parameters(ICommandExecutor commands) => (IReadOnlyDictionary<string, object?>)commands.ReceivedCalls().Single().GetArguments()[1]!;
}
