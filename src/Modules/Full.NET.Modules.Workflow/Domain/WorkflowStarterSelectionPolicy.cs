using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>发起人选择是发布版本约束下的稳定输入，不接受客户端自报范围或通过截断角色名册证明授权。</summary>
internal sealed record WorkflowStarterSelectionPolicy(string SelectionKey, string ModeKey, int MaximumUsers, WorkflowStarterSelectionRange Range)
{
    public const int MaximumSelections = 64;
    public const int MaximumTotalSelectedUsers = 160;

    public static bool TryRead(JsonElement value, out WorkflowStarterSelectionPolicy? policy)
    {
        policy = null;
        if (!Exact(value, "selectionKey", "modeKey", "maximumUsers", "range") ||
            !ReadString(value, "selectionKey", out var key) || !WorkflowNodeKeyValidator.IsValid(key) ||
            !ReadString(value, "modeKey", out var mode) || mode is not ("single" or "multiple") ||
            value.GetProperty("maximumUsers").ValueKind != JsonValueKind.Number || !value.GetProperty("maximumUsers").TryGetInt32(out var maximum) ||
            maximum is < 1 or > 20 || mode == "single" && maximum != 1) return false;
        var range = value.GetProperty("range");
        if (range.ValueKind != JsonValueKind.Object || !ReadString(range, "kindKey", out var kind)) return false;
        Guid[] users = []; Guid[] roles = [];
        var valid = kind switch
        {
            "all" => Exact(range, "kindKey"),
            "specified_users" => Exact(range, "kindKey", "userIds") && ReadIds(range.GetProperty("userIds"), 20, out users),
            "role_members" => Exact(range, "kindKey", "roleIds") && ReadIds(range.GetProperty("roleIds"), 5, out roles),
            _ => false,
        };
        if (!valid) return false;
        policy = new(key!, mode!, maximum, new(kind!, users, roles));
        return true;
    }

    internal static bool ReadIds(JsonElement value, int maximum, out Guid[] ids)
    {
        ids = [];
        if (value.ValueKind != JsonValueKind.Array) return false;
        var items = value.EnumerateArray().Take(maximum + 1).ToArray();
        if (items.Length < 1 || items.Length > maximum) return false;
        var result = new List<Guid>(); var seen = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.String || !Guid.TryParseExact(item.GetString(), "D", out var id) || id == Guid.Empty || !seen.Add(id)) return false;
            result.Add(id);
        }
        ids = result.ToArray(); return true;
    }
    private static bool ReadString(JsonElement value, string key, out string? result)
    {
        result = null;
        if (!value.TryGetProperty(key, out var item) || item.ValueKind != JsonValueKind.String) return false;
        result = item.GetString(); return true;
    }
    private static bool Exact(JsonElement value, params string[] keys) => value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).SequenceEqual(keys.Order(StringComparer.Ordinal));
}

internal sealed record WorkflowStarterSelectionRange(string KindKey, IReadOnlyList<Guid> UserIds, IReadOnlyList<Guid> RoleIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record WorkflowStarterSelectionSnapshot(int SchemaVersion, IReadOnlyDictionary<string, IReadOnlyList<Guid>> Selections);
