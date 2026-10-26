using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>流程管理员只是固定版本中的明确办理名单，不隐式授予平台管理权限。</summary>
internal static class WorkflowFlowAdministrators
{
    public static bool TryRead(JsonElement startConfig, out IReadOnlyList<Guid> users)
    {
        users = [];
        if (startConfig.ValueKind != JsonValueKind.Object) return false;
        if (!startConfig.TryGetProperty("administratorUserIds", out var list)) return true;
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 20) return false;
        var result = new List<Guid>(); var seen = new HashSet<Guid>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !Guid.TryParse(item.GetString(), out var id) || id == Guid.Empty || !seen.Add(id)) return false;
            result.Add(id);
        }
        users = result.ToArray(); return true;
    }
}
