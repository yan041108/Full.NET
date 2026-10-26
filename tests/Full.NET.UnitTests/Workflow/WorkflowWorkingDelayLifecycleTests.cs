using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>生命周期只消费固定日历和原实例，工作量不使用墙钟差或最新发布版本。</summary>
[TestClass]
public sealed class WorkflowWorkingDelayLifecycleTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Pause_keeps_only_remaining_work_and_resume_skips_the_weekend(DatabaseProvider provider)
    {
        var row=Row();var query=Query([row]);var commands=Commands(1);
        Assert.IsTrue(await WorkflowWorkingDelayLifecycle.ApplyAsync(row.InstanceId,"host","pause",At("2026-10-23T16:30:00Z"),provider,query,commands,default));
        var paused=Parameters(commands);Assert.AreEqual(90*60_000L,paused["RemainingMilliseconds"]);Assert.AreEqual(row.Revision,paused["Revision"]);
        commands.ClearReceivedCalls();row=row with{PausedAtUtc=At("2026-10-23T16:30:00Z"),RemainingMilliseconds=90*60_000,Revision=8};query=Query([row]);
        Assert.IsTrue(await WorkflowWorkingDelayLifecycle.ApplyAsync(row.InstanceId,"host","resume",At("2026-10-24T09:00:00Z"),provider,query,commands,default));
        Assert.AreEqual(At("2026-10-26T10:30:00Z"),Parameters(commands)["DueAtUtc"]);Assert.AreEqual(1,query.ReceivedCalls().Count());
        var queryParameters=(IReadOnlyDictionary<string,object?>)query.ReceivedCalls().Single().GetArguments()[1]!;Assert.AreEqual(129,queryParameters["Take"]);
    }
    [TestMethod]
    public async Task A_foreign_instance_missing_version_or_changed_snapshot_cannot_update_a_wait()
    {
        var original=Row();WorkflowWorkingDelayRecord[] invalid=[original with{InstanceId=Guid.NewGuid()},original with{TenantScopeKey="tenant:"+Guid.NewGuid().ToString("N")},
            original with{CalendarVersionId=null},original with{CalendarHash=new string('f',64)},original with{CalendarJson="{}"},original with{TimeZoneKey="Asia/Shanghai"}];
        foreach(var row in invalid)
        {
            var commands=Commands(1);Assert.IsFalse(await WorkflowWorkingDelayLifecycle.ApplyAsync(original.InstanceId,"host","pause",At("2026-10-23T16:30:00Z"),DatabaseProvider.SqlServer,Query([row]),commands,default));
            Assert.AreEqual(0,commands.ReceivedCalls().Count());
        }
    }
    [TestMethod]
    public async Task Stale_revision_or_excessive_active_waits_fail_the_whole_control_operation()
    {
        var row=Row();Assert.IsFalse(await WorkflowWorkingDelayLifecycle.ApplyAsync(row.InstanceId,"host","pause",At("2026-10-23T16:30:00Z"),DatabaseProvider.SqlServer,Query([row]),Commands(0),default));
        var commands=Commands(1);Assert.IsFalse(await WorkflowWorkingDelayLifecycle.ApplyAsync(row.InstanceId,"host","pause",At("2026-10-23T16:30:00Z"),DatabaseProvider.SqlServer,Query(Enumerable.Repeat(row,129).ToArray()),commands,default));
        Assert.AreEqual(0,commands.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Resume_does_not_freeze_a_timer_that_was_never_explicitly_paused()
    {
        var row=Row();var commands=Commands(1);
        Assert.IsTrue(await WorkflowWorkingDelayLifecycle.ApplyAsync(row.InstanceId,"host","resume",At("2026-10-24T09:00:00Z"),DatabaseProvider.SqlServer,Query([row]),commands,default));
        Assert.AreEqual(0,commands.ReceivedCalls().Count());
    }
    private static WorkflowWorkingDelayRecord Row()
    {
        Assert.IsTrue(WorkflowWorkCalendar.TryCreate(new(1,"UTC",Enumerable.Range(1,5).Select(day=>new WorkflowWorkCalendarWeekday(day,[new("09:00","17:00")])).ToArray()),out var calendar));
        return new(Guid.CreateVersion7(),Guid.CreateVersion7(),"host",At("2026-10-26T10:00:00Z"),null,null,7,Guid.CreateVersion7(),calendar!.ContentHash,calendar.CanonicalJson,"UTC");
    }
    private static IQueryExecutor Query(IReadOnlyList<WorkflowWorkingDelayRecord> rows)
    {var query=Substitute.For<IQueryExecutor>();query.QueryAsync<WorkflowWorkingDelayRecord>(Arg.Any<SqlStatement>(),Arg.Any<object?>(),Arg.Any<CancellationToken>()).Returns(Task.FromResult(rows));return query;}
    private static ICommandExecutor Commands(int affected)
    {var commands=Substitute.For<ICommandExecutor>();commands.ExecuteAsync(Arg.Any<SqlStatement>(),Arg.Any<object?>(),Arg.Any<CancellationToken>()).Returns(Task.FromResult(affected));return commands;}
    private static IReadOnlyDictionary<string,object?> Parameters(ICommandExecutor commands)=>(IReadOnlyDictionary<string,object?>)commands.ReceivedCalls().Single().GetArguments()[1]!;
    private static DateTimeOffset At(string value)=>DateTimeOffset.Parse(value,System.Globalization.CultureInfo.InvariantCulture);
}
