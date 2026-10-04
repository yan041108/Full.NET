using System.Text.Json;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    // 工具不引入运行时 Composition 依赖；名称准入与真实模块解析的一致性由回归测试约束。
    private static readonly string[] DiagnosticModulePresets =
    [
        "Full", "Minimal", "Platform", "Content", "Saas", "Enterprise",
    ];

    private static void CheckModulePreset(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        const string enabledPath = "FullNet:Modules:Enabled";
        var hasEnabledChildren = ReadRuntimeConfigurationPaths(root, profileSettings, workspacePath, profile)
            .Any(path => path.StartsWith(enabledPath + ":", StringComparison.OrdinalIgnoreCase));
        _ = TryReadDatabaseValue(root, profileSettings, workspacePath, profile, enabledPath, out var enabled);
        // 子键或空字符串数组标记可绑定列表；非空标量不能绑定数组，仍由预设选择模块。
        // 空父节点不删除已有子键；本切片不认证显式列表合法性。
        if (hasEnabledChildren || enabled == string.Empty) return;

        var hasPreset = TryReadDatabaseValue(root, profileSettings, workspacePath, profile,
            "FullNet:Modules:Preset", out var preset);
        // 只有缺键才保留 Options 的 Full 默认值；显式 null、空集合或空白均不能恢复默认名。
        if (!hasPreset) preset = "Full";
        findings.Add(DiagnosticModulePresets.Contains(preset, StringComparer.OrdinalIgnoreCase)
            ? DiagnoseFinding.Ok("DIAG_MODULE_PRESET_CONFIGURED",
                "最终生效的模块预设名称有效；未认证预设成员、模块依赖或宿主启动。")
            : DiagnoseFinding.Error("DIAG_MODULE_PRESET_INVALID",
                "最终生效的模块预设名称无效。",
                "核对 FullNet:Modules:Preset，使用 Full、Minimal、Platform、Content、Saas 或 Enterprise；名称忽略大小写但不忽略空白。显式 Enabled 列表覆盖预设，诊断不会回显配置值。"));
    }
}
