using System.Text.Json;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    // 工具不引入运行时 Composition 依赖；名称准入与真实模块解析的一致性由回归测试约束。
    private static readonly string[] DiagnosticModulePresets =
    [
        "Full", "Minimal", "Platform", "Content", "Saas", "Enterprise",
    ];

    // 仅表示官方稳定键准入；逐项与宿主官方全集对照，不据此推断裁剪应用安装了实现。
    private static readonly string[] DiagnosticModuleNames =
    [
        "Identity", "Auditing", "Files", "Document", "Notifications", "Calendar", "Platform", "Regions",
        "Jobs", "Messaging", "Tenancy", "Organization", "ImportExport", "Reporting", "Printing", "Ai",
        "Settings", "CodeGeneration", "SerialNumbers", "DataApproval", "ObservabilityAdmin", "Workflow",
        "Mqtt", "Webhooks", "Cryptography", "Payments", "GoView", "K3Cloud", "Ocr", "EnterpriseRequest",
    ];

    private sealed record DiagnosticModuleSelection(string? Preset, IReadOnlySet<string>? Enabled);

    private static DiagnosticModuleSelection? CheckModulePreset(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        const string enabledPath = "FullNet:Modules:Enabled";
        var paths = ReadRuntimeConfigurationPaths(root, profileSettings, workspacePath, profile);
        var prefix = enabledPath + ":";
        var children = paths.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(path => path[prefix.Length..].Split(':')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _ = TryReadDatabaseValue(root, profileSettings, workspacePath, profile, enabledPath, out var enabled);
        // 子键或空字符串数组标记可绑定列表；非空标量不能绑定数组，仍由预设选择模块。
        // 空父节点不删除已有子键；列表绑定后无需再认证被覆盖的预设。
        if (children.Length > 0 || enabled == string.Empty)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var valid = true;
            foreach (var child in children)
            {
                var path = prefix + child;
                var hasValue = TryReadDatabaseValue(root, profileSettings, workspacePath, profile, path, out var name);
                // Binder 跳过不能构造字符串的对象项；null 与空数组项保留，按空名称拒绝。
                if (name is null && paths.Any(key => key.StartsWith(path + ":", StringComparison.OrdinalIgnoreCase))) continue;
                if (!hasValue || string.IsNullOrWhiteSpace(name)
                    || !DiagnosticModuleNames.Contains(name, StringComparer.Ordinal) || !names.Add(name))
                    valid = false;
            }
            valid &= names.Contains("Identity");
            findings.Add(valid
                ? DiagnoseFinding.Ok("DIAG_MODULE_ENABLED_CONFIGURED",
                    "最终生效的 Enabled 列表名称有效、无重复且包含 Identity；未认证模块实现可用性、依赖闭包或宿主启动。")
                : DiagnoseFinding.Error("DIAG_MODULE_ENABLED_INVALID",
                    "最终生效的 Enabled 列表为空、名称无效、有重复项或缺少 Identity。",
                    "核对 FullNet:Modules:Enabled，使用非空、区分大小写的官方稳定模块键且不得重复，必须包含 Identity；名称不忽略空白。列表覆盖 Preset，子键按配置来源合并；诊断不会回显名称或配置值。"));
            return valid ? new DiagnosticModuleSelection(null, names) : null;
        }

        var hasPreset = TryReadDatabaseValue(root, profileSettings, workspacePath, profile,
            "FullNet:Modules:Preset", out var preset);
        // 只有缺键才保留 Options 的 Full 默认值；显式 null、空集合或空白均不能恢复默认名。
        if (!hasPreset) preset = "Full";
        var validPreset = DiagnosticModulePresets.Contains(preset, StringComparer.OrdinalIgnoreCase);
        findings.Add(validPreset
            ? DiagnoseFinding.Ok("DIAG_MODULE_PRESET_CONFIGURED",
                "最终生效的模块预设名称有效；未认证预设成员、模块依赖或宿主启动。")
            : DiagnoseFinding.Error("DIAG_MODULE_PRESET_INVALID",
                "最终生效的模块预设名称无效。",
                "核对 FullNet:Modules:Preset，使用 Full、Minimal、Platform、Content、Saas 或 Enterprise；名称忽略大小写但不忽略空白。显式 Enabled 列表覆盖预设，诊断不会回显配置值。"));
        return validPreset ? new DiagnosticModuleSelection(preset, null) : null;
    }
}
