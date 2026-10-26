using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>多实例来源先固化为有界项；角色成员必须来自事务外受信目录，不能用请求中的任意用户数组替代。</summary>
internal sealed record WorkflowMultiInstancePolicy(string ModeKey, string SourceKey, int MaximumCount, int? FixedCount,
    string? FieldKey, Guid? RoleId, string? ItemFieldKey, int RequiredPercentage, string EmptyModeKey,
    IReadOnlyDictionary<string, string> OutputReducers)
{
    internal const int MaximumItems = 32;
    public static bool TryRead(JsonElement value, WorkflowFormSchema? parent, [NotNullWhen(true)] out WorkflowMultiInstancePolicy? policy)
    {
        policy = null;
        if (!WorkflowSubprocessPolicy.Unique(value) || value.EnumerateObject().Count() != 5 ||
            value.EnumerateObject().Any(item => item.Name is not ("modeKey" or "source" or "requiredPercentage" or "emptyModeKey" or "outputReducers")) ||
            !Text(value, "modeKey", out var mode) || mode is not ("serial" or "parallel") ||
            !Text(value, "emptyModeKey", out var empty) || empty is not ("reject" or "skip") ||
            !value.TryGetProperty("requiredPercentage", out var percentage) || percentage.ValueKind != JsonValueKind.Number || !percentage.TryGetInt32(out var required) || required is < 1 or > 100 ||
            !value.TryGetProperty("source", out var source) || !WorkflowSubprocessPolicy.Unique(source) || !Text(source, "kindKey", out var kind) ||
            !value.TryGetProperty("outputReducers", out var reducers) || !WorkflowSubprocessPolicy.Unique(reducers) || reducers.EnumerateObject().Count() > WorkflowSubprocessPolicy.MaximumMappings) return false;
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reducer in reducers.EnumerateObject())
        {
            if (!WorkflowSubprocessPolicy.Key(reducer.Name) || reducer.Value.ValueKind != JsonValueKind.String ||
                reducer.Value.GetString() is not ("first" or "last" or "sum" or "min" or "max" or "all_true" or "any_true" or "append")) return false;
            output.Add(reducer.Name, reducer.Value.GetString()!);
        }
        int maximum; int? count = null; string? field = null; string? itemField = null; Guid? roleId = null;
        if (kind == "fixed_count")
        {
            if (source.EnumerateObject().Count() != 2 || !source.TryGetProperty("count", out var amount) || amount.ValueKind != JsonValueKind.Number || !amount.TryGetInt32(out maximum)) return false;
            count = maximum;
        }
        else
        {
            if (!source.TryGetProperty("maximumCount", out var cap) || cap.ValueKind != JsonValueKind.Number || !cap.TryGetInt32(out maximum)) return false;
            if (kind is "number_field" or "collection_field")
            {
                if (!Text(source, "fieldKey", out field) || !WorkflowSubprocessPolicy.Key(field)) return false;
                var expectedType = kind == "number_field" ? "integer" : "checkbox";
                if (parent is not null && !parent.Sections.SelectMany(section => section.Fields).Any(item => item.FieldKey == field && item.FieldTypeKey == expectedType)) return false;
            }
            else if (kind == "role_members")
            {
                if (!source.TryGetProperty("roleId", out var role) || !WorkflowSubprocessPolicy.ReadId(role, out var id)) return false;
                roleId = id;
            }
            else return false;
            if (kind is "collection_field" or "role_members")
            {
                if (source.EnumerateObject().Count() != 4 || !Text(source, "itemFieldKey", out itemField) || !WorkflowSubprocessPolicy.Key(itemField)) return false;
            }
            else if (source.EnumerateObject().Count() != 3) return false;
        }
        if (maximum is < 1 or > MaximumItems) return false;
        policy = new(mode!, kind!, maximum, count, field, roleId, itemField, required, empty!, output); return true;
    }

    public int RequiredSuccesses(int count) => count is > 0 and <= MaximumItems ? (count * RequiredPercentage + 99) / 100 : 0;

    public bool TryFreeze(IReadOnlyDictionary<string, JsonElement> input, IReadOnlyList<Guid>? trustedRoleMembers,
        out IReadOnlyList<WorkflowMultiInstanceSeed> items)
    {
        items = []; var values = new List<string>();
        if (SourceKey == "fixed_count") values.AddRange(Enumerable.Range(1, FixedCount!.Value).Select(number => number.ToString(CultureInfo.InvariantCulture)));
        else if (SourceKey == "number_field")
        {
            if (!input.TryGetValue(FieldKey!, out var amount) || amount.ValueKind != JsonValueKind.Number || !amount.TryGetInt32(out var count) || count < 0 || count > MaximumCount) return false;
            values.AddRange(Enumerable.Range(1, count).Select(number => number.ToString(CultureInfo.InvariantCulture)));
        }
        else if (SourceKey == "collection_field")
        {
            if (!input.TryGetValue(FieldKey!, out var collection) || collection.ValueKind != JsonValueKind.Array || collection.GetArrayLength() > MaximumCount) return false;
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in collection.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 512 } text || !distinct.Add(text)) return false;
                values.Add(value.GetRawText());
            }
        }
        else if (SourceKey == "role_members")
        {
            if (trustedRoleMembers is null || trustedRoleMembers.Count > MaximumCount || trustedRoleMembers.Any(id => id == Guid.Empty) || trustedRoleMembers.Distinct().Count() != trustedRoleMembers.Count) return false;
            values.AddRange(trustedRoleMembers.Select(id => id.ToString("D")).Order(StringComparer.Ordinal).Select(id => "\"" + id + "\""));
        }
        else return false;
        if (values.Count > MaximumCount || values.Count == 0 && EmptyModeKey != "skip") return false;
        items = values.Select((json, index) => new WorkflowMultiInstanceSeed("item-" + (index + 1).ToString("D4", CultureInfo.InvariantCulture), index + 1, json)).ToArray();
        return true;
    }
    private static bool Text(JsonElement value, string name, out string? text)
    { text = null; if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false; text = property.GetString(); return text is not null; }
}

internal sealed record WorkflowMultiInstanceSeed(string ItemKey, int SequenceNo, string ValueJson);
