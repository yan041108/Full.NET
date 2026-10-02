using System.Text.Json.Serialization;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 模块启用校验问题。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Code 为稳定错误码前缀，发布后不可改名。</remarks>
/// <param name="Code">稳定问题码；发布后不可改名或删除。</param>
/// <param name="Message">面向人工阅读的说明文案；不作为机器匹配依据。</param>
/// <param name="ModuleKey">问题所属模块键；跨模块问题时为 <see langword="null"/>。</param>
/// <param name="RelatedModuleKey">关联模块键；用于依赖缺失类问题，无关联时为 <see langword="null"/>。</param>
public sealed record ModuleSelectionIssueResponse(
    string Code,
    string Message,
    string? ModuleKey,
    string? RelatedModuleKey);

/// <summary>
/// 单个官方模块在启用集中的状态。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Dependencies/MissingDependencies 为稳定模块键集合，发布后不可改名。</remarks>
/// <param name="ModuleKey">模块键；发布后不可改名。</param>
/// <param name="IsEnabled">当前是否在启用集中。</param>
/// <param name="Dependencies">声明的依赖模块键集合。</param>
/// <param name="MissingDependencies">启用集中缺失的依赖模块键集合；无缺失时为空集合。</param>
public sealed record ModuleSelectionModuleStateResponse(
    string ModuleKey,
    bool IsEnabled,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> MissingDependencies);

/// <summary>
/// 运行时或候选模块启用集分析结果。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。SourceKind/DeploymentNotice 为稳定机器码，发布后不可改名；Issues/Modules 顺序按 Schema 固定，不应在序列化端重排。</remarks>
/// <param name="IsValid">启用集是否通过校验。</param>
/// <param name="SourceKind">来源类型键；区分运行时、候选配置等。</param>
/// <param name="Preset">所用预设键；无预设时为 <see langword="null"/>。</param>
/// <param name="EnabledModuleKeys">已启用模块键集合。</param>
/// <param name="OfficialModuleKeys">官方模块键集合；用于校验候选是否覆盖官方范围。</param>
/// <param name="Issues">校验问题集合；IsValid 为 false 时非空。</param>
/// <param name="Modules">各官方模块状态集合；顺序固定。</param>
/// <param name="DeploymentNotice">部署提示键；用于前端展示注意事项。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Enabled 与 Preset 互斥优先级发布后不可调整；<see cref="JsonUnmappedMemberHandling.Disallow"/> 拒绝未知字段，调用方必须按 Schema 升级。</remarks>
/// <param name="Preset">预设键；启用集来自预设时使用，无预设时为 <see langword="null"/>。</param>
/// <param name="Enabled">显式启用模块键集合；非空时覆盖 <see cref="Preset"/>。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModuleSelectionValidateRequest(
    string? Preset,
    IReadOnlyList<string>? Enabled);
