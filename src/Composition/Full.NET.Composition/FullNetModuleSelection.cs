using Full.NET.Modularity.Modules;
using Microsoft.Extensions.Configuration;

namespace Full.NET.Composition;

/// <summary>
/// 解析 <c>FullNet:Modules</c> 配置并校验启用集与模块依赖 DAG 一致。
/// </summary>
public static class FullNetModuleSelection
{
    public static readonly IReadOnlyList<string> OfficialModuleNames =
    [
        "Identity",
        "Auditing",
        "Files",
        "Document",
        "Notifications",
        "Jobs",
        "Messaging",
        "Tenancy",
        "Organization",
        "Settings",
        "CodeGeneration",
        "SerialNumbers",
    ];

    public static readonly IReadOnlyList<string> MinimalPresetModuleNames =
    [
        "Identity",
        "Tenancy",
        "Settings",
        "Organization",
    ];

    public static IReadOnlySet<string> ResolveEnabledNames(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetSection(FullNetModuleSelectionOptions.SectionName)
            .Get<FullNetModuleSelectionOptions>()
            ?? new FullNetModuleSelectionOptions();

        IReadOnlyList<string> enabledNames;
        if (options.Enabled is { Length: > 0 })
        {
            enabledNames = options.Enabled;
        }
        else if (string.Equals(
                     options.Preset,
                     FullNetModuleSelectionOptions.Presets.Minimal,
                     StringComparison.OrdinalIgnoreCase))
        {
            enabledNames = MinimalPresetModuleNames;
        }
        else
        {
            enabledNames = OfficialModuleNames;
        }

        ValidateEnabledNames(enabledNames);
        return enabledNames.ToHashSet(StringComparer.Ordinal);
    }

    public static IReadOnlyList<IFullNetModule> FilterModules(
        IReadOnlyList<IFullNetModule> allModules,
        IReadOnlySet<string> enabledNames) =>
        allModules
            .Where(module => enabledNames.Contains(module.Name))
            .ToArray();

    internal static void ValidateEnabledNames(IReadOnlyList<string> enabledNames)
    {
        if (enabledNames.Count == 0)
        {
            throw new InvalidOperationException(
                "FullNet:Modules 启用集不能为空；删除 Enabled 并使用 Preset=Full 恢复完整注册。");
        }

        var official = OfficialModuleNames.ToHashSet(StringComparer.Ordinal);
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in enabledNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException(
                    "FullNet:Modules:Enabled 包含空模块名。");
            }

            if (!official.Contains(name))
            {
                throw new InvalidOperationException(
                    $"FullNet:Modules 启用未知模块“{name}”；"
                    + $"可选值：{string.Join(", ", OfficialModuleNames)}。");
            }

            if (!enabled.Add(name))
            {
                throw new InvalidOperationException(
                    $"FullNet:Modules:Enabled 重复声明模块“{name}”。");
            }
        }

        if (!enabled.Contains("Identity"))
        {
            throw new InvalidOperationException(
                "FullNet:Modules 启用集必须包含 Identity。");
        }
    }

    public static IReadOnlyList<IFullNetModule> ResolveEnabledModules(
        IConfiguration configuration,
        IReadOnlyList<IFullNetModule> allModules)
    {
        var enabledNames = ResolveEnabledNames(configuration);
        var modules = FilterModules(allModules, enabledNames);
        ValidateDependencies(modules, enabledNames);
        return modules;
    }

    internal static void ValidateDependencies(
        IReadOnlyList<IFullNetModule> enabledModules,
        IReadOnlySet<string> enabledNames)
    {
        foreach (var module in enabledModules)
        {
            foreach (var dependency in module.Dependencies)
            {
                if (!enabledNames.Contains(dependency))
                {
                    throw new InvalidOperationException(
                        $"模块“{module.Name}”依赖“{dependency}”，"
                        + "但 FullNet:Modules 启用集未包含该依赖。");
                }
            }
        }
    }
}
