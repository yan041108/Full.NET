namespace Full.NET.Modularity.Modules;

using Microsoft.Extensions.Configuration;

/// <summary>
/// 模块启用配置来源稳定机器码。
/// </summary>
public static class ModuleSelectionSourceKinds
{
    /// <summary>由 <c>FullNet:Modules:Preset</c> 解析。</summary>
    public const string Preset = "preset";

    /// <summary>由 <c>FullNet:Modules:Enabled</c> 显式列表解析。</summary>
    public const string Explicit = "explicit";
}

/// <summary>
/// 模块启用校验问题稳定机器码。
/// </summary>
public static class ModuleSelectionIssueCodes
{
    public const string UnknownPreset = "modules.unknown_preset";

    public const string EmptyEnabled = "modules.empty_enabled";

    public const string BlankModuleName = "modules.blank_module_name";

    public const string UnknownModule = "modules.unknown_module";

    public const string DuplicateModule = "modules.duplicate_module";

    public const string MissingIdentity = "modules.missing_identity";

    public const string MissingDependency = "modules.missing_dependency";

    public const string InvalidOptionalDependency = "modules.invalid_optional_dependency";
}

/// <summary>
/// 表示模块启用集分析中的单个问题。
/// </summary>
/// <param name="Code">稳定机器码，见 <see cref="ModuleSelectionIssueCodes"/>。</param>
/// <param name="Message">面向运维与开发者的英文说明。</param>
/// <param name="ModuleKey">问题关联模块键，可为空。</param>
/// <param name="RelatedModuleKey">相关依赖或冲突模块键，可为空。</param>
public sealed record ModuleSelectionIssue(
    string Code,
    string Message,
    string? ModuleKey,
    string? RelatedModuleKey);

/// <summary>
/// 表示官方模块在启用集中的状态摘要。
/// </summary>
/// <param name="ModuleKey">稳定模块键。</param>
/// <param name="IsEnabled">当前分析结果下是否启用。</param>
/// <param name="Dependencies">模块声明的硬依赖。</param>
/// <param name="MissingDependencies">已启用检查时尚未满足的硬依赖。</param>
public sealed record ModuleSelectionModuleState(
    string ModuleKey,
    bool IsEnabled,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> MissingDependencies);

/// <summary>
/// 表示一次模块启用集分析结果。
/// </summary>
public sealed class ModuleSelectionAnalysis
{
    /// <summary>启用集是否通过名称与 DAG 校验。</summary>
    public required bool IsValid { get; init; }

    /// <summary>配置来源，见 <see cref="ModuleSelectionSourceKinds"/>。</summary>
    public required string SourceKind { get; init; }

    /// <summary>使用的预设名称；显式列表时为空。</summary>
    public string? Preset { get; init; }

    /// <summary>解析后的启用模块键，按 Ordinal 排序。</summary>
    public required IReadOnlyList<string> EnabledModuleKeys { get; init; }

    /// <summary>官方模块全集，与 Composition 编译闭包一致。</summary>
    public required IReadOnlyList<string> OfficialModuleKeys { get; init; }

    /// <summary>校验问题；通过时为空。</summary>
    public required IReadOnlyList<ModuleSelectionIssue> Issues { get; init; }

    /// <summary>各官方模块在启用集中的状态。</summary>
    public required IReadOnlyList<ModuleSelectionModuleState> ModuleStates { get; init; }

    /// <summary>部署期生效说明，提醒不可运行时热加载。</summary>
    public required string DeploymentNotice { get; init; }
}

/// <summary>
/// 分析运行时或候选 <c>FullNet:Modules</c> 配置，不修改 DI 注册。
/// </summary>
public interface IFullNetModuleSelectionPreview
{
    /// <summary>读取当前进程配置并分析启用集。</summary>
    /// <param name="configuration">宿主配置根。</param>
    ModuleSelectionAnalysis AnalyzeRuntime(IConfiguration configuration);

    /// <summary>分析候选预设或显式启用列表。</summary>
    /// <param name="preset">候选预设；与 <paramref name="enabled"/> 同时提供时以显式列表为准。</param>
    /// <param name="enabled">候选显式启用模块键。</param>
    ModuleSelectionAnalysis AnalyzeCandidate(
        string? preset,
        IReadOnlyList<string>? enabled);
}
