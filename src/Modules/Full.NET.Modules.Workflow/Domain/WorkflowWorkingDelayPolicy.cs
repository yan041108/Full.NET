using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>工作时长必须绑定真实发布快照，不能把客户端时区或未发布日历当作运行策略。</summary>
internal static class WorkflowWorkingDelayPolicy
{
    public static bool TryRead(JsonElement value,out WorkflowDelayPolicy? policy)
    {
        policy=null;
        if(value.ValueKind!=JsonValueKind.Object || value.EnumerateObject().Select(property=>property.Name).Distinct(StringComparer.Ordinal).Count()!=value.EnumerateObject().Count() ||
            value.EnumerateObject().Any(property=>property.Name is not("modeKey" or "timeZoneKey" or "duration" or "unitKey" or "calendarVersionId" or "boundCalendarHash" or "boundCalendar")) ||
            !String(value,"modeKey",out var mode) || mode!="working" || !String(value,"timeZoneKey",out var zone) || zone!.Length>128 ||
            !String(value,"calendarVersionId",out var version) || !Guid.TryParseExact(version,"D",out var id) || id==Guid.Empty ||
            !String(value,"boundCalendarHash",out var hash) || hash!.Length!=64 || !String(value,"unitKey",out var unit) ||
            !value.TryGetProperty("duration",out var duration) || duration.ValueKind!=JsonValueKind.Number || !duration.TryGetInt64(out var amount) || amount<1 ||
            !value.TryGetProperty("boundCalendar",out var snapshot) || !WorkflowWorkCalendar.TryRead(snapshot,out var calendar) || calendar!.ContentHash!=hash || calendar.TimeZoneKey!=zone)return false;
        var multiplier=unit switch{"second"=>1L,"minute"=>60L,"hour"=>3600L,"day"=>86400L,_=>0L};
        if(multiplier==0 || amount>WorkflowDelayPolicy.MaximumDurationSeconds/multiplier)return false;
        policy=new("working",zone,amount*multiplier,null,null,id,hash,calendar);return true;
    }
    private static bool String(JsonElement value,string key,out string? text)
    {text=null;if(!value.TryGetProperty(key,out var property) || property.ValueKind!=JsonValueKind.String)return false;text=property.GetString();return !string.IsNullOrWhiteSpace(text);}
}
