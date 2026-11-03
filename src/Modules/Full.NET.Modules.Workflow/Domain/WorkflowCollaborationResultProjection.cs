using System.Text;
using System.Text.Json;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>只接纳固定返回字段；已向目标展示的输入或待覆盖字段变化时，不能把旧审批应用到新输入。</summary>
internal static class WorkflowCollaborationResultProjection
{
    internal static bool TryMerge(WorkflowFormSchema schema, WorkflowCollaborationAcceptancePolicy accepted,
        IReadOnlyDictionary<string, JsonElement> original, IReadOnlyDictionary<string, JsonElement> current, string projectionJson,
        out WorkflowFieldMutationOutput? output, out string error, bool verifiedFiles = false)
    {
        output = null; error = "workflow.collaboration.output_invalid";
        if (projectionJson is null || Encoding.UTF8.GetByteCount(projectionJson) > 131_072) return false;
        try
        {
            using var document = JsonDocument.Parse(projectionJson);
            if (!WorkflowVotePolicy.UniqueObject(document.RootElement)) return false;
            var patch = JsonSerializer.Deserialize(document.RootElement, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            if (patch is null || patch.Count > 64 || patch.Keys.Any(key => !accepted.OutputFields.Values.Contains(key, StringComparer.Ordinal))) return false;
            var fields = schema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
            foreach (var pair in patch)
                if (!fields.TryGetValue(pair.Key, out var field) || !WorkflowCollaborationValueProjection.Valid(field, pair.Value, verifiedFiles)) return false;
            foreach (var key in accepted.InputFields.Keys.Concat(patch.Keys).Distinct(StringComparer.Ordinal))
                if (!Same(original, current, key)) { error = "workflow.collaboration.input_conflict"; return false; }
            var next = current.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
            foreach (var pair in patch) next[pair.Key] = pair.Value.Clone();
            if (!WorkflowFormValueValidator.Validate(schema, next)) return false;
            output = new(next, patch, patch.Count > 0); return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool Same(IReadOnlyDictionary<string, JsonElement> original, IReadOnlyDictionary<string, JsonElement> current, string key) =>
        original.TryGetValue(key, out var before) == current.TryGetValue(key, out var after) &&
        (before.ValueKind == JsonValueKind.Undefined || JsonElement.DeepEquals(before, after));
}
