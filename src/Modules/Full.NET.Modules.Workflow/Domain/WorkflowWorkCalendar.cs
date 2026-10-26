using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>工作日历的闭合草稿；发布后固定时区、每周窗口与逐日例外。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowWorkCalendarDraft(int SchemaVersion,string TimeZoneKey,
    IReadOnlyList<WorkflowWorkCalendarWeekday> Weekdays,IReadOnlyList<WorkflowWorkCalendarException>? Exceptions=null);

/// <summary>星期使用0到6表示周日到周六，缺省星期没有工作窗口。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowWorkCalendarWeekday(int DayOfWeek,IReadOnlyList<WorkflowWorkCalendarRange> Ranges);

/// <summary>例外日覆盖当天全部每周窗口，空窗口表示休息日。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowWorkCalendarException(string Date,IReadOnlyList<WorkflowWorkCalendarRange> Ranges);

/// <summary>本地半开时间段，格式为HH:mm；仅结束允许24:00。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowWorkCalendarRange(string Start,string End);

/// <summary>已发布快照同时固定时区规则，操作系统更新不能改变在途实例的恢复语义。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowWorkCalendarSnapshot(int SchemaVersion,string TimeZoneKey,string TimeZoneRules,
    IReadOnlyList<WorkflowWorkCalendarWeekday> Weekdays,IReadOnlyList<WorkflowWorkCalendarException>? Exceptions=null);

/// <summary>按固定日历计算真实UTC工作交集；拒绝夏令时歧义与无界截止搜索。</summary>
internal sealed class WorkflowWorkCalendar
{
    internal const int MaximumSearchDays=3660;
    internal const long MaximumWorkMilliseconds=WorkflowDelayPolicy.MaximumDurationSeconds*1000;
    private readonly TimeZoneInfo zone;
    private readonly IReadOnlyDictionary<int,CalendarRange[]> weekdays;
    private readonly IReadOnlyDictionary<DateOnly,CalendarRange[]> exceptions;
    private WorkflowWorkCalendar(TimeZoneInfo zone,Dictionary<int,CalendarRange[]> weekdays,Dictionary<DateOnly,CalendarRange[]> exceptions,string json)
    {this.zone=zone;this.weekdays=weekdays;this.exceptions=exceptions;CanonicalJson=json;ContentHash=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));}
    public string CanonicalJson{get;}
    public string ContentHash{get;}
    public string TimeZoneKey=>zone.Id;

    public static bool TryRead(JsonElement value,out WorkflowWorkCalendar? calendar)
    {
        calendar=null;
        try
        {
            var snapshot=value.Deserialize(WorkflowJsonSerializerContext.Default.WorkflowWorkCalendarSnapshot);
            if(snapshot is not{SchemaVersion:1} || string.IsNullOrEmpty(snapshot.TimeZoneRules) || snapshot.TimeZoneRules.Length>65536)return false;
            var zone=TimeZoneInfo.FromSerializedString(snapshot.TimeZoneRules);
            if(zone.Id!=snapshot.TimeZoneKey)return false;
            return TryBuild(new(snapshot.SchemaVersion,snapshot.TimeZoneKey,snapshot.Weekdays,snapshot.Exceptions),zone,out calendar);
        }
        catch(Exception error) when(error is JsonException or ArgumentException or System.Runtime.Serialization.SerializationException){return false;}
    }

    public static bool TryCreate(WorkflowWorkCalendarDraft? draft,out WorkflowWorkCalendar? calendar)
    {
        calendar=null;
        if(draft is not{SchemaVersion:1} || string.IsNullOrWhiteSpace(draft.TimeZoneKey) || draft.TimeZoneKey.Length>128 ||
            !TimeZoneInfo.TryFindSystemTimeZoneById(draft.TimeZoneKey,out var zone))return false;
        return TryBuild(draft,zone,out calendar);
    }

    private static bool TryBuild(WorkflowWorkCalendarDraft draft,TimeZoneInfo zone,out WorkflowWorkCalendar? calendar)
    {
        calendar=null;
        if(draft.Weekdays is null || draft.Weekdays.Count>7 || (draft.Exceptions?.Count??0)>366)return false;
        var weekdays=new Dictionary<int,CalendarRange[]>();var exceptions=new Dictionary<DateOnly,CalendarRange[]>();
        foreach(var day in draft.Weekdays)
        {
            if(day is null || day.DayOfWeek is <0 or >6 || weekdays.ContainsKey(day.DayOfWeek) || !TryRanges(day.Ranges,out var ranges))return false;
            weekdays.Add(day.DayOfWeek,ranges);
        }
        foreach(var day in draft.Exceptions??[])
        {
            if(day is null || !DateOnly.TryParseExact(day.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date) || date.Year is <1000 or >=9999 ||
                exceptions.ContainsKey(date) || !TryRanges(day.Ranges,out var ranges))return false;
            exceptions.Add(date,ranges);
        }
        if(!weekdays.Values.Any(ranges=>ranges.Length!=0) && !exceptions.Values.Any(ranges=>ranges.Length!=0))return false;
        var json=WorkflowJsonCanonicalizer.Compile(writer=>{
            writer.WriteStartObject();writer.WriteNumber("schemaVersion",1);writer.WriteString("timeZoneKey",zone.Id);
            writer.WriteString("timeZoneRules",zone.ToSerializedString());
            writer.WriteStartArray("weekdays");
            foreach(var pair in weekdays.OrderBy(pair=>pair.Key))
            {writer.WriteStartObject();writer.WriteNumber("dayOfWeek",pair.Key);WriteRanges(writer,pair.Value);writer.WriteEndObject();}
            writer.WriteEndArray();writer.WriteStartArray("exceptions");
            foreach(var pair in exceptions.OrderBy(pair=>pair.Key))
            {writer.WriteStartObject();writer.WriteString("date",pair.Key.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));WriteRanges(writer,pair.Value);writer.WriteEndObject();}
            writer.WriteEndArray();writer.WriteEndObject();
        }).CanonicalJson;
        if(Encoding.UTF8.GetByteCount(json)>131072)return false;
        calendar=new(zone,weekdays,exceptions,json);return true;
    }

    public bool TryAddWorkingMilliseconds(DateTimeOffset start,long milliseconds,out DateTimeOffset due)
    {
        due=default;
        if(milliseconds is <0 or >MaximumWorkMilliseconds || !TryLocalDate(start,out var date))return false;
        if(milliseconds==0){due=start.ToUniversalTime();return true;}
        var remaining=milliseconds*TimeSpan.TicksPerMillisecond;
        for(var offset=0;offset<MaximumSearchDays;offset++)
        {
            if(!TryUtcRanges(date,out var ranges))return false;
            foreach(var range in ranges)
            {
                var cursor=range.Start>start?range.Start:start.ToUniversalTime();
                if(range.End<=cursor)continue;
                var available=(range.End-cursor).Ticks;
                if(remaining<=available){due=cursor.AddTicks(remaining);return due.Year<9999;}
                remaining-=available;
            }
            if(date.Year>=9998 && date.Month==12 && date.Day==31)return false;
            date=date.AddDays(1);
        }
        return false;
    }

    public bool TryRemainingWorkingMilliseconds(DateTimeOffset start,DateTimeOffset end,out long milliseconds)
    {
        milliseconds=0;
        if(!TryLocalDate(start,out var date) || !TryLocalDate(end,out var last))return false;
        if(end<=start)return true;
        var days=last.DayNumber-date.DayNumber;
        if(days is <0 or >=MaximumSearchDays)return false;
        long ticks=0;
        for(var offset=0;offset<=days;offset++)
        {
            if(!TryUtcRanges(date,out var ranges))return false;
            foreach(var range in ranges)
            {
                var from=range.Start>start?range.Start:start;var to=range.End<end?range.End:end;
                if(to>from)ticks+=(to-from).Ticks;
            }
            if(offset<days)date=date.AddDays(1);
        }
        // 持久化精度为毫秒，向上取整避免暂停恢复提前唤醒。
        milliseconds=(ticks+TimeSpan.TicksPerMillisecond-1)/TimeSpan.TicksPerMillisecond;
        return milliseconds<=MaximumWorkMilliseconds;
    }

    private bool TryLocalDate(DateTimeOffset instant,out DateOnly date)
    {
        date=default;if(instant.Year is <1000 or >=9999)return false;
        var local=TimeZoneInfo.ConvertTime(instant,zone);if(local.Year is <1000 or >=9999)return false;
        date=DateOnly.FromDateTime(local.DateTime);return true;
    }
    private bool TryUtcRanges(DateOnly date,out IReadOnlyList<UtcRange> ranges)
    {
        ranges=[];
        var windows=exceptions.TryGetValue(date,out var special)?special:weekdays.GetValueOrDefault((int)date.DayOfWeek)??[];
        var result=new List<UtcRange>(windows.Length);var midnight=date.ToDateTime(TimeOnly.MinValue,DateTimeKind.Unspecified);
        foreach(var window in windows)
        {
            var from=midnight.AddMinutes(window.StartMinutes);var to=midnight.AddMinutes(window.EndMinutes);
            if(zone.IsInvalidTime(from) || zone.IsInvalidTime(to) || zone.IsAmbiguousTime(from) || zone.IsAmbiguousTime(to))return false;
            var begin=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(from,zone));var end=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(to,zone));
            if(begin>=end)return false;result.Add(new(begin,end));
        }
        ranges=result;return true;
    }
    private static bool TryRanges(IReadOnlyList<WorkflowWorkCalendarRange>? values,out CalendarRange[] ranges)
    {
        ranges=[];if(values is null || values.Count>8)return false;
        var result=new List<CalendarRange>();
        foreach(var value in values)
        {
            if(value is null || !TryMinute(value.Start,false,out var start) || !TryMinute(value.End,true,out var end) || start>=end)return false;
            result.Add(new(start,end));
        }
        ranges=result.OrderBy(range=>range.StartMinutes).ToArray();
        for(var index=1;index<ranges.Length;index++)if(ranges[index].StartMinutes<ranges[index-1].EndMinutes)return false;
        return true;
    }
    private static bool TryMinute(string? value,bool allowEnd,out int minute)
    {
        minute=0;if(allowEnd && value=="24:00"){minute=1440;return true;}
        if(value is null || value.Length!=5 || !TimeOnly.TryParseExact(value,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))return false;
        minute=time.Hour*60+time.Minute;return true;
    }
    private static void WriteRanges(Utf8JsonWriter writer,IEnumerable<CalendarRange> ranges)
    {
        static string Format(int minute)=>string.Concat((minute/60).ToString("00",CultureInfo.InvariantCulture),":",(minute%60).ToString("00",CultureInfo.InvariantCulture));
        writer.WriteStartArray("ranges");foreach(var range in ranges)
        {writer.WriteStartObject();writer.WriteString("start",Format(range.StartMinutes));writer.WriteString("end",Format(range.EndMinutes));writer.WriteEndObject();}
        writer.WriteEndArray();
    }
    private sealed record CalendarRange(int StartMinutes,int EndMinutes);
    private sealed record UtcRange(DateTimeOffset Start,DateTimeOffset End);
}
