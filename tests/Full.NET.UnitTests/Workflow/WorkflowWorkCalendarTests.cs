using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>工作时长必须按固定窗口计算，不能把墙钟剩余量加到休息日。</summary>
[TestClass]
public sealed class WorkflowWorkCalendarTests
{
    [TestMethod]
    public void Working_duration_skips_weekend_and_lunch()
    {
        var calendar=Week();
        Assert.IsTrue(calendar.TryAddWorkingMilliseconds(At("2026-10-23T16:00:00Z"),Hours(2),out var due));
        Assert.AreEqual(At("2026-10-26T10:00:00Z"),due);
        Assert.IsTrue(calendar.TryAddWorkingMilliseconds(At("2026-10-26T11:30:00Z"),Hours(1),out due));
        Assert.AreEqual(At("2026-10-26T13:30:00Z"),due);
    }
    [TestMethod]
    public void Exception_day_replaces_weekly_windows_including_an_extra_workday()
    {
        var calendar=Week([new("2026-10-26",[]),new("2026-10-24",[new("10:00","12:00")])]);
        Assert.IsTrue(calendar.TryAddWorkingMilliseconds(At("2026-10-23T16:00:00Z"),Hours(4),out var due));
        Assert.AreEqual(At("2026-10-27T10:00:00Z"),due);
        Assert.IsTrue(calendar.TryAddWorkingMilliseconds(At("2026-10-24T09:00:00Z"),Hours(1),out due));
        Assert.AreEqual(At("2026-10-24T11:00:00Z"),due);
    }
    [TestMethod]
    public void Pausing_then_resuming_on_a_weekend_preserves_work_instead_of_wall_time()
    {
        var calendar=Week();
        Assert.IsTrue(calendar.TryRemainingWorkingMilliseconds(At("2026-10-23T16:30:00Z"),At("2026-10-26T10:00:00Z"),out var remaining));
        Assert.AreEqual(90*60*1000L,remaining);
        Assert.IsTrue(calendar.TryAddWorkingMilliseconds(At("2026-10-24T09:00:00Z"),remaining,out var due));
        Assert.AreEqual(At("2026-10-26T10:30:00Z"),due);
    }
    [TestMethod]
    public void Window_boundaries_are_half_open_and_end_of_day_is_supported()
    {
        Assert.IsTrue(WorkflowWorkCalendar.TryCreate(new(1,"UTC",[new(5,[new("23:00","24:00")])]),out var calendar));
        Assert.IsTrue(calendar!.TryAddWorkingMilliseconds(At("2026-10-23T23:00:00Z"),Hours(1),out var due));
        Assert.AreEqual(At("2026-10-24T00:00:00Z"),due);
        Assert.IsTrue(calendar.TryRemainingWorkingMilliseconds(due,due,out var zero));Assert.AreEqual(0,zero);
        Assert.IsTrue(calendar.TryAddWorkingMilliseconds(due,0,out var same));Assert.AreEqual(due,same);
    }
    [TestMethod]
    [DataRow("2026-03-08T05:00:00Z",3,"2026-03-08T08:00:00Z")]
    [DataRow("2026-11-01T04:00:00Z",5,"2026-11-01T09:00:00Z")]
    public void Daylight_transition_uses_actual_utc_work_intersection(string start,int hours,string expected)
    {
        Assert.IsTrue(WorkflowWorkCalendar.TryCreate(new(1,"America/New_York",[new(0,[new("00:00","04:00")])]),out var calendar));
        Assert.IsTrue(calendar!.TryAddWorkingMilliseconds(At(start),Hours(hours),out var due));Assert.AreEqual(At(expected),due);
        Assert.IsTrue(calendar.TryRemainingWorkingMilliseconds(At(start),due,out var remaining));Assert.AreEqual(Hours(hours),remaining);
    }
    [TestMethod]
    [DataRow("2026-03-08T05:00:00Z","02:30","03:30")]
    [DataRow("2026-11-01T04:00:00Z","01:30","03:00")]
    public void Missing_or_ambiguous_local_window_endpoint_is_rejected(string start,string from,string to)
    {
        Assert.IsTrue(WorkflowWorkCalendar.TryCreate(new(1,"America/New_York",[new(0,[new(from,to)])]),out var calendar));
        Assert.IsFalse(calendar!.TryAddWorkingMilliseconds(At(start),1,out _));
    }
    [TestMethod]
    public void Invalid_versions_ranges_duplicates_and_empty_calendars_are_rejected()
    {
        WorkflowWorkCalendarDraft[] invalid=[new(2,"UTC",[]),new(1,"bad-zone",[new(1,[new("09:00","10:00")])]),new(1,"UTC",[]),
            new(1,"UTC",[new(1,[new("09:00","12:00"),new("11:00","13:00")])]),new(1,"UTC",[new(1,[new("24:00","24:00")])]),
            new(1,"UTC",[new(1,[new("9:00","10:00")])]),new(1,"UTC",[new(1,[new("09:00","10:00")]),new(1,[])]),
            new(1,"UTC",[new(1,[new("09:00","10:00")])],[new("2026-10-26",[]),new("2026-10-26",[])])];
        foreach(var draft in invalid)Assert.IsFalse(WorkflowWorkCalendar.TryCreate(draft,out _));
        Assert.IsFalse(WorkflowWorkCalendar.TryRead(JsonSerializer.SerializeToElement(new{schemaVersion=1,timeZoneKey="UTC",weekdays=Array.Empty<object>(),url="https://invalid"}),out _));
    }
    [TestMethod]
    public void Canonical_snapshot_is_independent_of_input_order_and_later_mutation()
    {
        var days=new List<WorkflowWorkCalendarWeekday>{new(2,[new("13:00","17:00"),new("09:00","12:00")]),new(1,[new("09:00","17:00")])};
        Assert.IsTrue(WorkflowWorkCalendar.TryCreate(new(1,"UTC",days),out var calendar));
        var hash=calendar!.ContentHash;days.Clear();
        using var document=JsonDocument.Parse(calendar.CanonicalJson);Assert.IsTrue(WorkflowWorkCalendar.TryRead(document.RootElement,out var restored));
        Assert.AreEqual(hash,restored!.ContentHash);Assert.AreEqual(calendar.CanonicalJson,restored.CanonicalJson);
        Assert.IsTrue(restored.TryAddWorkingMilliseconds(At("2026-10-26T09:00:00Z"),Hours(1),out var due));Assert.AreEqual(At("2026-10-26T10:00:00Z"),due);
    }
    [TestMethod]
    [DataRow("UTC")]
    [DataRow("Asia/Shanghai")]
    [DataRow("America/New_York")]
    public void Published_timezone_rules_roundtrip_without_reading_updated_system_rules(string zone)
    {
        Assert.IsTrue(WorkflowWorkCalendar.TryCreate(new(1,zone,[new(1,[new("09:00","17:00")])]),out var calendar));
        using var snapshot=JsonDocument.Parse(calendar!.CanonicalJson);
        Assert.IsTrue(snapshot.RootElement.GetProperty("timeZoneRules").GetString()!.Length>0);
        Assert.IsTrue(WorkflowWorkCalendar.TryRead(snapshot.RootElement,out var restored));Assert.AreEqual(calendar.ContentHash,restored!.ContentHash);
        Assert.IsTrue(calendar.TryAddWorkingMilliseconds(At("2026-10-26T00:00:00Z"),Hours(1),out var originalDue));
        Assert.IsTrue(restored.TryAddWorkingMilliseconds(At("2026-10-26T00:00:00Z"),Hours(1),out var restoredDue));Assert.AreEqual(originalDue,restoredDue);
    }
    [TestMethod]
    public void Search_and_remaining_work_are_bounded_and_rounding_does_not_wake_early()
    {
        Assert.IsTrue(WorkflowWorkCalendar.TryCreate(new(1,"UTC",[],[new("2038-01-01",[new("09:00","10:00")])]),out var distant));
        Assert.IsFalse(distant!.TryAddWorkingMilliseconds(At("2026-10-26T09:00:00Z"),1,out _));
        var calendar=Week();Assert.IsFalse(calendar.TryAddWorkingMilliseconds(At("2026-10-26T09:00:00Z"),WorkflowWorkCalendar.MaximumWorkMilliseconds+1,out _));
        Assert.IsTrue(calendar.TryRemainingWorkingMilliseconds(At("2026-10-26T09:00:00Z"),At("2026-10-26T09:00:00.000001Z"),out var remaining));Assert.AreEqual(1,remaining);
        Assert.IsFalse(calendar.TryRemainingWorkingMilliseconds(At("2026-10-26T09:00:00Z"),At("2038-01-01T09:00:00Z"),out _));
    }
    private static WorkflowWorkCalendar Week(WorkflowWorkCalendarException[]? exceptions=null)
    {
        Assert.IsTrue(WorkflowWorkCalendar.TryCreate(new(1,"UTC",Enumerable.Range(1,5).Select(day=>new WorkflowWorkCalendarWeekday(day,[new("09:00","12:00"),new("13:00","17:00")])).ToArray(),exceptions),out var result));return result!;
    }
    private static long Hours(int hours)=>hours*3600_000L;
    private static DateTimeOffset At(string value)=>DateTimeOffset.Parse(value,System.Globalization.CultureInfo.InvariantCulture);
}
