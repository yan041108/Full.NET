using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>异步回填允许独立字段前进；原目标被覆盖时失败，不能刷新修订后重放旧全量表单。</summary>
internal static class WorkflowAsyncOutputPolicy
{
    public static bool TryMap(WorkflowConnectorPolicy policy, IReadOnlyDictionary<string, JsonElement> original,
        IReadOnlyDictionary<string, JsonElement> current, string responseJson, out WorkflowFieldMutationOutput? output)
    {
        output = null;
        return TargetsUnchanged(policy,original,current) && policy.TryMapOutput(current,responseJson,out output);
    }

    internal static bool TargetsUnchanged(WorkflowConnectorPolicy policy,IReadOnlyDictionary<string,JsonElement> original,IReadOnlyDictionary<string,JsonElement> current)
    {
        foreach (var field in policy.OutputFields.Keys)
        {
            var hadValue = original.TryGetValue(field, out var before);
            var hasValue = current.TryGetValue(field, out var now);
            if (hadValue != hasValue || hadValue && Canonical(before) != Canonical(now)) return false;
        }
        return true;
    }

    private static string Canonical(JsonElement value) => WorkflowJsonCanonicalizer.Compile(writer =>
    {
        writer.WriteStartObject(); writer.WritePropertyName("value"); value.WriteTo(writer); writer.WriteEndObject();
    }).CanonicalJson;
}
