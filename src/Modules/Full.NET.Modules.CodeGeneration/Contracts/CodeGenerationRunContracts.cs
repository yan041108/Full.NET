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

    public const string GitSyncFailed = "codegen.run.git_sync_failed";

    public const string GitPublishFailed = "codegen.run.git_publish_failed";
}

/// <summary>
/// 表示一次受跟踪预览的输入；内联 Schema 与模板版本必须严格二选一。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationRunPreviewRequest(
    Guid? TemplateId,
    long? TemplateVersion,
    CodeGenerationPreviewRequest? Schema);

/// <summary>
/// 表示成功持久化摘要后返回的预览结果。
/// </summary>
public sealed record CodeGenerationRunPreviewResponse(
    Guid RunId,
    CodeGenerationPreviewResponse Preview);

/// <summary>
/// 表示一次绑定已审查预览的 Apply 请求；工作区和源码均不得由客户端指定。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationRunApplyRequest(Guid PreviewRunId);

/// <summary>
/// 表示本地工作区成功提交后的稳定摘要，不暴露服务器路径或生成源码。
/// </summary>
public sealed record CodeGenerationRunApplyResponse(
    Guid RunId,
    Guid PreviewRunId,
    int ArtifactCount,
    int ChangedArtifactCount,
    string ManifestSha256);

/// <summary>
/// 表示回滚已成功 Apply 的请求；仅允许 applyRunId，禁止客户端指定路径。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationRunRollbackRequest(Guid ApplyRunId);

/// <summary>
/// 表示工作区逆向提交后的稳定摘要，不暴露服务器路径或生成源码。
/// </summary>
public sealed record CodeGenerationRunRollbackResponse(
    Guid RunId,
    Guid ApplyRunId,
    int ArtifactCount,
    int ChangedArtifactCount,
    string ManifestSha256);

/// <summary>
/// 表示按 LIFO 顺序回滚多个已成功 Apply；禁止客户端指定路径。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationRunRollbackChainRequest(
    IReadOnlyList<Guid> ApplyRunIds);

/// <summary>
/// 表示链式回滚各步的稳定摘要集合。
/// </summary>
public sealed record CodeGenerationRunRollbackChainResponse(
    IReadOnlyList<CodeGenerationRunRollbackResponse> Rollbacks);

/// <summary>
/// 表示不包含 Schema、源码或异常正文的代码生成运行摘要。
/// </summary>
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