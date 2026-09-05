using Full.NET.Modularity.Modules;
using Microsoft.Extensions.Configuration;

namespace Full.NET.Composition;

/// <summary>
/// 解析 <c>FullNet:Modules</c> 配置并校验启用集与模块依赖 DAG 一致。
/// </summary>
public static class FullNetModuleSelection
{
    /// <summary>
    /// 官方模块稳定键全集，与 <see cref="FullNetModuleCatalog"/> 编译闭包一一对应；
    /// 启用集校验只接受其中的名称，禁止运行时登记未声明模块。
    /// </summary>
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
        "DataApproval",
        "ObservabilityAdmin",
        "Workflow",
    ];

    /// <summary>
    /// Minimal 预设模块键：Identity + Tenancy + Settings + Organization。
    /// </summary>
    /// <remarks>仅提供租户与组织底座，供最小化部署或测试裁剪使用。</remarks>
    public static readonly IReadOnlyList<string> MinimalPresetModuleNames =
    [
        "Identity",
        "Tenancy",
        "Settings",
        "Organization",
    ];

    /// <summary>
    /// Platform 预设模块键：Minimal 基础上追加 Auditing、Notifications、Jobs、Messaging 平台能力。
    /// </summary>
    public static readonly IReadOnlyList<string> PlatformPresetModuleNames =
    [
        "Identity",
        "Tenancy",
        "Settings",
        "Organization",
        "Auditing",
        "Notifications",
        "Jobs",
        "Messaging",
        "ObservabilityAdmin",
    ];

    /// <summary>
    /// Content 预设模块键：Platform 基础上追加 Files、Document 内容管理能力。
    /// </summary>
    public static readonly IReadOnlyList<string> ContentPresetModuleNames =
    [
        "Identity",
        "Tenancy",
        "Settings",
        "Organization",
        "Auditing",
        "Notifications",
        "Jobs",
        "Messaging",
        "Files",
        "Document",
        "ObservabilityAdmin",
    ];

    /// <summary>
    /// 读取 <c>FullNet:Modules</c> 配置解析出启用模块稳定键集合，并校验名称合法、无重复且包含 Identity。
    /// </summary>
    /// <param name="configuration">宿主配置根，必须包含 <c>FullNet:Modules</c> 节。</param>
    /// <returns>启用模块名称集合，使用 <see cref="StringComparer.Ordinal"/> 比较以保持稳定匹配。</returns>
    /// <exception cref="InvalidOperationException">启用集为空、含空名、含未知模块名或缺少 Identity。</exception>
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
        else if (string.Equals(
                     options.Preset,
                     FullNetModuleSelectionOptions.Presets.Platform,
                     StringComparison.OrdinalIgnoreCase))
        {
            enabledNames = PlatformPresetModuleNames;
        }
        else if (string.Equals(
                     options.Preset,
                     FullNetModuleSelectionOptions.Presets.Content,
                     StringComparison.OrdinalIgnoreCase))
        {
            enabledNames = ContentPresetModuleNames;
        }
        else
        {
            enabledNames = OfficialModuleNames;
        }

        ValidateEnabledNames(enabledNames);
        return enabledNames.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// 按启用名称集合从全量模块列表筛出待注册子集；不在此处校验依赖。
    /// </summary>
    /// <param name="allModules">由组合根构造的全量官方模块实例列表。</param>
    /// <param name="enabledNames">已校验通过的启用模块名称集合。</param>
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

    /// <summary>
    /// 组合解析与过滤：先解析启用名称，再筛选模块实例，最后校验依赖 DAG 在启用集内闭合。
    /// </summary>
    /// <param name="configuration">宿主配置根。</param>
    /// <param name="allModules">由组合根构造的全量官方模块实例列表。</param>
    /// <exception cref="InvalidOperationException">启用集非法或某模块依赖未在启用集中。</exception>
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
            foreach (var optionalDependency in module.OptionalContractDependencies)
            {
                if (!OfficialModuleNames.Contains(optionalDependency, StringComparer.Ordinal) ||
                    module.Dependencies.Contains(optionalDependency, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"模块“{module.Name}”包含无效或重复的可选契约依赖“{optionalDependency}”。");
                }
            }

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
