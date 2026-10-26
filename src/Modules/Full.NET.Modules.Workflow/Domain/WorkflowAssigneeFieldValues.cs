using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>动态人员/部门来源复用发布表单的单选、多选引用；稳定标识仍须交由所属模块复核。</summary>
internal static class WorkflowAssigneeFieldValues
{
    public static bool Supports(WorkflowFormField field)
    {
        if (field.FieldTypeKey is not ("select" or "checkbox") || field.Constraints is null ||
            !WorkflowFormChoiceOptions.TryRead(field, out var options)) return false;
        var seen = new HashSet<Guid>();
        return options.All(value => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty && seen.Add(id));
    }

    public static bool TryRead(JsonElement value, out IReadOnlyList<Guid> identifiers)
    {
        identifiers = [];
        var items = value.ValueKind switch
        {
            JsonValueKind.String => new[] { value },
            JsonValueKind.Array => value.EnumerateArray().Take(21).ToArray(),
            _ => [],
        };
        if (items.Length is < 1 or > 20) return false;
        var result = new List<Guid>(items.Length);
        var seen = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.String || !Guid.TryParseExact(item.GetString(), "D", out var id) || id == Guid.Empty || !seen.Add(id)) return false;
            result.Add(id);
        }
        identifiers = result;
        return true;
    }
}
