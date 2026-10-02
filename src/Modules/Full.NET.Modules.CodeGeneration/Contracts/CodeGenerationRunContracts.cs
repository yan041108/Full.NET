using System.Text.Json.Serialization;

namespace Full.NET.Modules.CodeGeneration.Contracts;

/// <summary>
/// 定义 Host 代码生成运行目录的读取与执行权限边界。
/// </summary>
public static class CodeGenerationRunPermissions
{
    public const string Read = "codegen.runs.read";

    public const string Execute = "codegen.runs.execute";

    public const string Apply = "codegen.runs.apply";

    /// <summary>
    /// 回滚已成功 Apply；不得复用 apply/execute 权限。
    /// </summary>
    public const string Rollback = "codegen.runs.rollback";

    public const string Download = "codegen.runs.download";
}

/// <summary>
/// 定义代码生成运行支持的稳定操作机器码。
/// </summary>
public static class CodeGenerationRunOperationKinds
{
    public const string Preview = "preview";

    public const string Apply = "apply";

    public const string Rollback = "rollback";
}

/// <summary>
/// 定义代码生成运行的稳定结果机器码。
/// </summary>
public static class CodeGenerationRunStatuses
{
    public const string Running = "running";

    public const string Succeeded = "succeeded";

    public const string Failed = "failed";
}

/// <summary>
/// 定义代码生成运行对外返回的稳定错误码。
/// </summary>
public static class CodeGenerationRunErrorCodes
{
    public const string InvalidSource = "codegen.run.invalid_source";

    public const string TemplateVersionConflict =
        "codegen.run.template_version_conflict";

    public const string GenerationFailed = "codegen.run.generation_failed";

    public const string InvalidQuery = "codegen.run.invalid_query";

    public const string NotFound = "codegen.run.not_found";

    public const string ApplyDisabled = "codegen.run.apply_disabled";

    public const string InvalidApplyPreview =
        "codegen.run.invalid_apply_preview";

    public const string StaleApplyPreview =
        "codegen.run.stale_apply_preview";

    public const string ApplyConflict = "codegen.run.apply_conflict";

    public const string ApplyBusy = "codegen.run.apply_busy";

    public const string ApplyFailed = "codegen.run.apply_failed";

    public const string RollbackDisabled = "codegen.run.rollback_disabled";

    public const string InvalidRollbackApply =
        "codegen.run.invalid_rollback_apply";

    public const string RollbackAlreadyApplied =
        "codegen.run.rollback_already_applied";

    public const string RollbackCheckpointMissing =
        "codegen.run.rollback_checkpoint_missing";

    public const string RollbackConflict = "codegen.run.rollback_conflict";

    public const string RollbackBusy = "codegen.run.rollback_busy";

    public const string RollbackFailed = "codegen.run.rollback_failed";

    public const string InvalidRollbackChain =
        "codegen.run.invalid_rollback_chain";

    public const string InvalidDownloadRun =
        "codegen.run.invalid_download_run";

    public const string GitSyncFailed = "codegen.run.git_sync_failed";

    public const string GitPublishFailed = "codegen.run.git_publish_failed";
}

/// <summary>
/// 表示一次受跟踪预览的输入；内联 Schema 与模板版本必须严格二选一。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 TemplateId 与 Schema 互斥，同时指定或同时为空将返回 InvalidQuery 错误。</remarks>
/// <param name="TemplateId">引用已持久化模板标识；采用 Schema 内联模式时为 <see langword="null"/>。</param>
/// <param name="TemplateVersion">模板期望版本号，用于 CAS 守卫；内联模式为 <see langword="null"/>。</param>
/// <param name="Schema">内联预览 Schema；模板模式为 <see langword="null"/>。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationRunPreviewRequest(
    Guid? TemplateId,
    long? TemplateVersion,
    CodeGenerationPreviewRequest? Schema);

/// <summary>
/// 表示成功持久化摘要后返回的预览结果。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 RunId 用于后续 Apply 的引用绑定。</remarks>
/// <param name="RunId">本次预览运行标识（UUID v7）。</param>
/// <param name="Preview">预览产物摘要，不含完整源码。</param>
public sealed record CodeGenerationRunPreviewResponse(
    Guid RunId,
    CodeGenerationPreviewResponse Preview);

/// <summary>
/// 表示一次绑定已审查预览的 Apply 请求；工作区和源码均不得由客户端指定。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 PreviewRunId 必须为存在且未过期的预览。</remarks>
/// <param name="PreviewRunId">绑定的预览运行标识。</param>
/// <param name="IntegrationTarget">模块接入目标；缺省时仅写入工作区产物，不接入模块。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationRunApplyRequest(
    Guid PreviewRunId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CodeGenerationIntegrationTargetRequest? IntegrationTarget = null);

/// <summary>
/// 调用方显式确认的模块接入目标；缺省时 Host Apply 只写入工作区产物。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 路径必须为相对工作区内受控目录，绝对路径或跨目录引用将被拒绝。</remarks>
/// <param name="ModuleName">目标模块稳定键，决定接入目录与注册位置。</param>
/// <param name="ModuleProjectPath">模块主项目文件相对路径。</param>
/// <param name="ModuleEntryPointPath">模块入口文件相对路径。</param>
/// <param name="CompositionProjectPath">组合项目文件相对路径。</param>
/// <param name="CompositionCatalogPath">组合目录文件相对路径。</param>
/// <param name="VueRouterPath">Vue 路由文件相对路径。</param>
/// <param name="LayuiRouterPath">Layui 路由文件相对路径；Vue-only 部署为 <see langword="null"/>。</param>
/// <param name="ClientRoute">显式前端路由映射；缺省时不接入路由。</param>
/// <param name="AuthorizationContributorPath">权限贡献器文件相对路径；无贡献为 <see langword="null"/>。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationIntegrationTargetRequest(
    string ModuleName,
    string ModuleProjectPath,
    string ModuleEntryPointPath,
    string CompositionProjectPath,
    string CompositionCatalogPath,
    string VueRouterPath,
    string? LayuiRouterPath = null,
    CodeGenerationClientRouteTargetRequest? ClientRoute = null,
    string? AuthorizationContributorPath = null);

/// <summary>显式 Vue 路由映射；Layui 路径缺省时只改 Vue。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 路径必须为相对工作区内受控目录。</remarks>
/// <param name="RoutePath">前端路由路径，发布后稳定。</param>
/// <param name="VueRouteName">Vue 路由名称，发布后稳定。</param>
/// <param name="VueComponentPath">Vue 组件文件相对路径。</param>
/// <param name="LayuiControllerPath">Layui 控制器文件相对路径；Vue-only 部署为 <see langword="null"/>。</param>
/// <param name="LayuiControllerExport">Layui 控制器导出名；未提供为 <see langword="null"/>。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationClientRouteTargetRequest(
    string RoutePath,
    string VueRouteName,
    string VueComponentPath,
    string? LayuiControllerPath = null,
    string? LayuiControllerExport = null);

/// <summary>
/// 表示本地工作区成功提交后的稳定摘要，不暴露服务器路径或生成源码。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 ManifestSha256 用于跨实例一致性比对，不可作为缓存键的唯一依据。</remarks>
/// <param name="RunId">本次 Apply 运行标识（UUID v7）。</param>
/// <param name="PreviewRunId">绑定的预览运行标识。</param>
/// <param name="ArtifactCount">本次写入的产物总数。</param>
/// <param name="ChangedArtifactCount">本次实际变更的产物数。</param>
/// <param name="ManifestSha256">产物清单 SHA-256 摘要。</param>
public sealed record CodeGenerationRunApplyResponse(
    Guid RunId,
    Guid PreviewRunId,
    int ArtifactCount,
    int ChangedArtifactCount,
    string ManifestSha256);

/// <summary>
/// 表示回滚已成功 Apply 的请求；仅允许 applyRunId，禁止客户端指定路径。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ApplyRunId">待回滚的 Apply 运行标识；必须存在且未被回滚。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationRunRollbackRequest(Guid ApplyRunId);

/// <summary>
/// 表示工作区逆向提交后的稳定摘要，不暴露服务器路径或生成源码。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="RunId">本次回滚运行标识（UUID v7）。</param>
/// <param name="ApplyRunId">回滚目标 Apply 运行标识。</param>
/// <param name="ArtifactCount">本次回滚涉及的产物总数。</param>
/// <param name="ChangedArtifactCount">本次实际变更的产物数。</param>
/// <param name="ManifestSha256">回滚后产物清单 SHA-256 摘要。</param>
public sealed record CodeGenerationRunRollbackResponse(
    Guid RunId,
    Guid ApplyRunId,
    int ArtifactCount,
    int ChangedArtifactCount,
    string ManifestSha256);

/// <summary>
/// 表示按 LIFO 顺序回滚多个已成功 Apply；禁止客户端指定路径。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 ApplyRunIds 顺序必须严格按 Apply 时间倒序，跨链或乱序将被拒绝。</remarks>
/// <param name="ApplyRunIds">待回滚的 Apply 运行标识列表，按 LIFO 顺序排列。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationRunRollbackChainRequest(
    IReadOnlyList<Guid> ApplyRunIds);

/// <summary>
/// 表示链式回滚各步的稳定摘要集合。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Rollbacks 顺序与请求 ApplyRunIds 一一对应。</remarks>
/// <param name="Rollbacks">链式回滚各步摘要集合，顺序与请求一致。</param>
public sealed record CodeGenerationRunRollbackChainResponse(
    IReadOnlyList<CodeGenerationRunRollbackResponse> Rollbacks);

/// <summary>
/// 表示不包含 Schema、源码或异常正文的代码生成运行摘要。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 OperationKind、Status、ErrorCode 为稳定机器码，发布后不得改名或删除。</remarks>
/// <param name="Id">运行标识（UUID v7）。</param>
/// <param name="TemplateId">引用模板标识；内联模式为 <see langword="null"/>。</param>
/// <param name="TemplateVersion">模板版本号；内联模式为 <see langword="null"/>。</param>
/// <param name="OperationKind">操作类型稳定机器码（preview/apply/rollback）。</param>
/// <param name="Status">运行状态稳定机器码（running/succeeded/failed）。</param>
/// <param name="ModuleKey">目标模块稳定键；未指定为 <see langword="null"/>。</param>
/// <param name="EntityKey">目标实体稳定键；未指定为 <see langword="null"/>。</param>
/// <param name="SchemaSha256">输入 Schema 的 SHA-256 摘要；无 Schema 为 <see langword="null"/>。</param>
/// <param name="ArtifactCount">本次运行产出的产物总数。</param>
/// <param name="ManifestSha256">产物清单 SHA-256 摘要；无产物为 <see langword="null"/>。</param>
/// <param name="ErrorCode">失败稳定错误码；成功为 <see langword="null"/>。</param>
/// <param name="RequestedByUserId">发起运行的用户标识。</param>
/// <param name="StartedAtUtc">运行开始时间（UTC）。</param>
/// <param name="FinishedAtUtc">运行结束时间（UTC）；运行中为 <see langword="null"/>。</param>
/// <param name="SourceApplyRunId">回滚操作指向的 Apply 运行标识；非回滚为 <see langword="null"/>。</param>
public sealed record CodeGenerationRunResponse(
    Guid Id,
    Guid? TemplateId,
    long? TemplateVersion,
    string OperationKind,
    string Status,
    string? ModuleKey,
    string? EntityKey,
    string? SchemaSha256,
    int ArtifactCount,
    string? ManifestSha256,
    string? ErrorCode,
    Guid RequestedByUserId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    Guid? SourceApplyRunId);