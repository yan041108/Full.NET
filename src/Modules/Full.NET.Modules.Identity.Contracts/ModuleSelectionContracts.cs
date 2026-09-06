using System.Text.Json.Serialization;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 模块启用校验问题。
/// </summary>
public sealed record ModuleSelectionIssueResponse(
    string Code,
    string Message,
    string? ModuleKey,
    string? RelatedModuleKey);

/// <summary>
/// 单个官方模块在启用集中的状态。
/// </summary>
public sealed record ModuleSelectionModuleStateResponse(
    string ModuleKey,
    bool IsEnabled,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> MissingDependencies);

/// <summary>
/// 运行时或候选模块启用集分析结果。
/// </summary>
public sealed record ModuleSelectionAnalysisResponse(
    bool IsValid,
    string SourceKind,
    string? Preset,
    IReadOnlyList<string> EnabledModuleKeys,
    IReadOnlyList<string> OfficialModuleKeys,
    IReadOnlyList<ModuleSelectionIssueResponse> Issues,
    IReadOnlyList<ModuleSelectionModuleStateResponse> Modules,
    string DeploymentNotice);

/// <summary>
/// 候选模块启用配置校验请求；显式 <see cref="Enabled"/> 非空时覆盖 <see cref="Preset"/>。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModuleSelectionValidateRequest(
    string? Preset,
    IReadOnlyList<string>? Enabled);
