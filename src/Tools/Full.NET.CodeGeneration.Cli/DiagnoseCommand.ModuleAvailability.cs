using System.Text.Json;
using System.Text.Json.Nodes;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private static void CheckStandaloneModuleAvailability(
        string workspacePath, DiagnosticModuleSelection selection, List<DiagnoseFinding> findings)
    {
        // 缺少项目或引用时由既有闭包检查报告，不能再认证运行选择可用。
        if (!findings.Any(finding => finding.Code == "DIAG_MODULE_CLOSURE_OK")) return;
        try
        {
            var app = JsonNode.Parse(File.ReadAllText(Path.Combine(workspacePath, "fullnet-app.json")));
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(workspacePath, "framework-manifest.json")));
            var presets = manifest?["presetModules"]?.AsObject() ?? throw new JsonException();
            var frozenPreset = app?["preset"]?.GetValue<string>() ?? throw new JsonException();
            var installed = ReadDiagnosticPresetMembers(presets, frozenPreset);
            IReadOnlySet<string> requested;
            if (selection.Enabled is { } enabled)
                requested = enabled;
            else if (string.Equals(selection.Preset, "Full", StringComparison.OrdinalIgnoreCase))
                // 应用投影器将 Full 的官方全集裁剪到冻结安装集，不能按仓库全集误拒绝。
                requested = installed;
            else if (string.Equals(selection.Preset, "Content", StringComparison.OrdinalIgnoreCase))
            {
                // 模板档案没有 Content 键；运行时定义为 Platform 加 Document。
                var content = ReadDiagnosticPresetMembers(presets, "Platform");
                content.Add("Document");
                requested = content;
            }
            else
                requested = ReadDiagnosticPresetMembers(presets, selection.Preset!);

            findings.Add(requested.All(installed.Contains)
                ? DiagnoseFinding.Ok("DIAG_RUNTIME_MODULES_AVAILABLE",
                    "依据冻结档案，最终模块选择处于安装范围内，预设项目与 Composition 引用齐全；未认证依赖图、编译或宿主启动。")
                : DiagnoseFinding.Error("DIAG_RUNTIME_MODULES_UNAVAILABLE",
                    "最终模块选择超出独立应用冻结的安装范围。",
                    "核对 FullNet:Modules:Preset/Enabled 与应用冻结预设；使用已安装模块，或通过已验证的模板升级安装范围。新增项目引用不能代替框架投影；诊断不会回显模块名或配置值。"));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException
            or ArgumentException or IOException or UnauthorizedAccessException)
        {
            findings.Add(DiagnoseFinding.Error("DIAG_RUNTIME_MODULES_METADATA_INVALID",
                "无法根据冻结档案确定最终模块选择的安装范围。",
                "检查 fullnet-app.json 与 framework-manifest.json 的预设成员；使用有效的官方模块键、无重复项且包含 Identity。诊断不会输出档案值或异常文本。"));
        }
    }

    private static HashSet<string> ReadDiagnosticPresetMembers(JsonObject presets, string preset)
    {
        // 按稳定预设键读取档案，不将配置值拼接为文件路径；歧义键不能静默选取。
        var matches = presets.Where(entry => string.Equals(entry.Key, preset, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].Value is not JsonArray entries || entries.Count == 0)
            throw new JsonException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var name = entry?.GetValue<string>();
            if (name is null || !DiagnosticModuleNames.Contains(name, StringComparer.Ordinal) || !names.Add(name))
                throw new JsonException();
        }
        if (!names.Contains("Identity")) throw new JsonException();
        return names;
    }
}
