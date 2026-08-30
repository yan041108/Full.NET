using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>读取并校验发布表单中的静态选项目录，供编译与运行时校验共享。</summary>
internal static class WorkflowFormChoiceOptions
{
    public static bool TryRead(WorkflowFormField field, out string[] options)
    {
        options = [];
        if (!field.Constraints.TryGetValue("options", out var configured) ||
            configured.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var items = configured.EnumerateArray().ToArray();
        if (items.Length == 0 || items.Any(item =>
                item.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(item.GetString())))
        {
            return false;
        }

        options = items.Select(item => item.GetString()!).ToArray();
        return options.Distinct(StringComparer.Ordinal).Count() == options.Length;
    }
}
