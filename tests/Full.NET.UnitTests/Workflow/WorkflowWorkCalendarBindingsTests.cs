using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>发布绑定只接纳真实作用域内的不可变版本，客户端绑定内容没有权威性。</summary>
[TestClass]
public sealed class WorkflowWorkCalendarBindingsTests
{
    [TestMethod]
    public async Task Publishing_replaces_client_binding_and_resolves_with_the_fixed_calendar()
    {
        var row=Version();var draft=Draft(row.Id);var query=Query([row]);
        Assert.IsFalse(WorkflowDelayPolicy.TryRead(draft.Nodes[1].Config,null,out _));
        var bound=await WorkflowWorkCalendarBindings.BindAsync(draft,query,new(null,"host","host"),default);Assert.IsNotNull(bound);
        var config=bound.Nodes[1].Config.GetProperty("delayPolicy");Assert.AreEqual(row.ContentHash,config.GetProperty("boundCalendarHash").GetString());
        Assert.IsTrue(config.GetProperty("boundCalendar").TryGetProperty("timeZoneRules",out _));
        Assert.AreEqual("untrusted",draft.Nodes[1].Config.GetProperty("delayPolicy").GetProperty("boundCalendarHash").GetString());
        Assert.IsTrue(WorkflowDelayPolicy.TryRead(bound.Nodes[1].Config,null,out var policy));
        Assert.IsTrue(policy!.TryResolve(new Dictionary<string,JsonElement>(),At("2026-10-23T16:00:00Z"),out var due));Assert.AreEqual(At("2026-10-26T10:00:00Z"),due);
        var call=query.ReceivedCalls().Single().GetArguments();var parameters=(IReadOnlyDictionary<string,object?>)call[1]!;Assert.AreEqual("host",parameters["TenantScopeKey"]);
        CollectionAssert.AreEqual(new[]{row.Id},(Guid[])parameters["Ids"]!);
    }
    [TestMethod]
    public async Task Missing_foreign_or_corrupt_versions_cannot_be_bound()
    {
        var row=Version();
        foreach(var records in new WorkflowCalendarVersionRecord[][]{[],[row with{TenantScopeKey="tenant:"+Guid.NewGuid().ToString("N")}],[row with{ContentHash=new string('f',64)}]})
            Assert.IsNull(await WorkflowWorkCalendarBindings.BindAsync(Draft(row.Id),Query(records),new(null,"host","host"),default));
        Assert.IsNull(await WorkflowWorkCalendarBindings.BindAsync(Draft(row.Id,"Asia/Shanghai"),Query([row]),new(null,"host","host"),default));
    }
    [TestMethod]
    public async Task A_wall_clock_delay_does_not_query_or_bind_a_calendar()
    {
        var query=Query([]);var draft=Draft(Guid.NewGuid()) with{Nodes=[Node("start","start",new{nextNodeKeys=new[]{"delay"}}),
            Node("delay","timer.delay",new{nextNodeKeys=new[]{"end"},delayPolicy=new{modeKey="relative",timeZoneKey="UTC",duration=1,unitKey="hour"}}),Node("end","end",new{})]};
        Assert.AreSame(draft,await WorkflowWorkCalendarBindings.BindAsync(draft,query,new(null,"host","host"),default));Assert.AreEqual(0,query.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task An_empty_version_or_duplicate_policy_property_is_rejected_before_reading_versions()
    {
        var query=Query([]);Assert.IsNull(await WorkflowWorkCalendarBindings.BindAsync(Draft(Guid.Empty),query,new(null,"host","host"),default));
        var draft=Draft(Guid.NewGuid());using var malformed=JsonDocument.Parse("{\"nextNodeKeys\":[\"end\"],\"delayPolicy\":{\"modeKey\":\"working\",\"modeKey\":\"working\",\"timeZoneKey\":\"UTC\"}}");
        draft=draft with{Nodes=[draft.Nodes[0],draft.Nodes[1] with{Config=malformed.RootElement.Clone()},draft.Nodes[2]]};
        Assert.IsNull(await WorkflowWorkCalendarBindings.BindAsync(draft,query,new(null,"host","host"),default));Assert.AreEqual(0,query.ReceivedCalls().Count());
    }
    private static WorkflowDefinitionDraft Draft(Guid version,string zone="UTC")=>new(1,[Node("start","start",new{nextNodeKeys=new[]{"delay"}}),
        Node("delay","timer.delay",new{nextNodeKeys=new[]{"end"},delayPolicy=new{modeKey="working",timeZoneKey=zone,duration=2,unitKey="hour",calendarVersionId=version,boundCalendarHash="untrusted",boundCalendar=new{malicious=true}}}),Node("end","end",new{})],"automation");
    private static WorkflowNodeDraft Node(string key,string type,object config)=>new(key,type,1,JsonSerializer.SerializeToElement(config));
    private static WorkflowCalendarVersionRecord Version()
    {
        Assert.IsTrue(WorkflowWorkCalendar.TryCreate(new(1,"UTC",Enumerable.Range(1,5).Select(day=>new WorkflowWorkCalendarWeekday(day,[new("09:00","17:00")])).ToArray()),out var calendar));
        return new(Guid.CreateVersion7(),Guid.CreateVersion7(),1,calendar!.CanonicalJson,calendar.ContentHash,"UTC",Guid.CreateVersion7(),DateTimeOffset.UtcNow,"host");
    }
    private static IQueryExecutor Query(IReadOnlyList<WorkflowCalendarVersionRecord> rows)
    {var query=Substitute.For<IQueryExecutor>();query.QueryAsync<WorkflowCalendarVersionRecord>(Arg.Any<SqlStatement>(),Arg.Any<object?>(),Arg.Any<CancellationToken>()).Returns(Task.FromResult(rows));return query;}
    private static DateTimeOffset At(string value)=>DateTimeOffset.Parse(value,System.Globalization.CultureInfo.InvariantCulture);
}
