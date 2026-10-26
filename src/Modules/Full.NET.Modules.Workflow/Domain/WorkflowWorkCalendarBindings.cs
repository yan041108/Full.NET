using System.Text.Json;
using System.Text;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>发布前批量绑定真实日历版本，覆盖客户端绑定内容，版本通过自有父日历继承作用域。</summary>
internal static class WorkflowWorkCalendarBindings
{
    public static async Task<WorkflowDefinitionDraft?> BindAsync(WorkflowDefinitionDraft draft,IQueryExecutor query,WorkflowManagementScope scope,CancellationToken token)
    {
        if(draft.Nodes is null || draft.Nodes.Count>WorkflowDefinitionCompiler.MaximumNodes)return null;
        var selections=new Dictionary<string,Guid>(StringComparer.Ordinal);
        foreach(var node in draft.Nodes)
        {
            if(node.NodeTypeKey!="timer.delay" || node.Config.ValueKind!=JsonValueKind.Object || !node.Config.TryGetProperty("delayPolicy",out var policy) ||
                policy.ValueKind!=JsonValueKind.Object || !policy.TryGetProperty("modeKey",out var mode) || mode.ValueKind!=JsonValueKind.String || mode.GetString()!="working")continue;
            if(!Unique(node.Config) || !Unique(policy) || !policy.TryGetProperty("calendarVersionId",out var version) || version.ValueKind!=JsonValueKind.String ||
                !Guid.TryParseExact(version.GetString(),"D",out var id) || id==Guid.Empty || !selections.TryAdd(node.NodeKey,id))return null;
        }
        if(selections.Count==0)return draft;
        var requested=selections.Values.Distinct().ToArray();
        var rows=await query.QueryAsync<WorkflowCalendarVersionRecord>(WorkflowCalendarSql.FindVersionsByIds,
            WorkflowSqlParameters.Create(("Ids",requested),("TenantScopeKey",scope.TenantScopeKey)),token).ConfigureAwait(false);
        if(rows.Count!=requested.Length || rows.Select(row=>row.Id).Distinct().Count()!=rows.Count || rows.Any(row=>row.TenantScopeKey!=scope.TenantScopeKey || !requested.Contains(row.Id)))return null;
        var calendars=new Dictionary<Guid,(WorkflowWorkCalendar Calendar,JsonElement Snapshot)>();
        try
        {
            foreach(var row in rows)
            {
                if(Encoding.UTF8.GetByteCount(row.CanonicalJson)>131072)return null;
                using var json=JsonDocument.Parse(row.CanonicalJson);
                if(!WorkflowWorkCalendar.TryRead(json.RootElement,out var calendar) || calendar!.ContentHash!=row.ContentHash || calendar.TimeZoneKey!=row.TimeZoneKey)return null;
                calendars.Add(row.Id,(calendar,json.RootElement.Clone()));
            }
            var nodes=new List<WorkflowNodeDraft>(draft.Nodes.Count);
            foreach(var node in draft.Nodes)
            {
                if(!selections.TryGetValue(node.NodeKey,out var id)){nodes.Add(node);continue;}
                var bound=calendars[id];var policy=node.Config.GetProperty("delayPolicy");
                if(!policy.TryGetProperty("timeZoneKey",out var zone) || zone.ValueKind!=JsonValueKind.String || zone.GetString()!=bound.Calendar.TimeZoneKey)return null;
                var config=WorkflowJsonCanonicalizer.Compile(writer=>{
                    writer.WriteStartObject();foreach(var property in node.Config.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        if(property.Name!="delayPolicy"){property.Value.WriteTo(writer);continue;}
                        writer.WriteStartObject();foreach(var entry in policy.EnumerateObject())
                            if(entry.Name is not ("boundCalendarHash" or "boundCalendar"))entry.WriteTo(writer);
                        writer.WriteString("boundCalendarHash",bound.Calendar.ContentHash);writer.WritePropertyName("boundCalendar");bound.Snapshot.WriteTo(writer);writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }).CanonicalJson;
                using var document=JsonDocument.Parse(config);nodes.Add(node with{Config=document.RootElement.Clone()});
            }
            return draft with{Nodes=nodes};
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException or ArgumentException){return null;}
    }
    private static bool Unique(JsonElement value)=>value.EnumerateObject().Select(property=>property.Name).Distinct(StringComparer.Ordinal).Count()==value.EnumerateObject().Count();
}
