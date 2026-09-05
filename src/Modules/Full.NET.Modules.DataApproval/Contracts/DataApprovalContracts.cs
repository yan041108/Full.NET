namespace Full.NET.Modules.DataApproval.Contracts;

/// <summary>DataApproval 请求状态机器键。</summary>
public static class DataApprovalStatusKeys
{
    /// <summary>已创建但尚未关联工作流。</summary>
    public const string Pending = "pending";

    /// <summary>工作流已启动，等待审批结论。</summary>
    public const string InReview = "in_review";

    /// <summary>审批通过且变更已应用或无需应用。</summary>
    public const string Approved = "approved";

    /// <summary>工作流驳回。</summary>
    public const string Rejected = "rejected";

    /// <summary>提交人或系统取消。</summary>
    public const string Cancelled = "cancelled";
}

/// <summary>DataApproval 模块权限码。</summary>
public static class DataApprovalPermissions
{
    /// <summary>读取审批请求列表与详情。</summary>
    public const string Read = "data_approvals.requests.read";

    /// <summary>创建审批请求。</summary>
    public const string Create = "data_approvals.requests.create";

    /// <summary>取消待处理审批请求。</summary>
    public const string Cancel = "data_approvals.requests.cancel";

    /// <summary>读取审批场景目录与绑定配置。</summary>
    public const string ScenariosRead = "data_approvals.scenarios.read";

    /// <summary>配置审批场景绑定与启停状态。</summary>
    public const string ScenariosManage = "data_approvals.scenarios.manage";
}

/// <summary>DataApproval 稳定错误码。</summary>
public static class DataApprovalErrorCodes
{
    /// <summary>请求体或查询参数无效。</summary>
    public const string RequestInvalid = "data_approvals.request.invalid";

    /// <summary>场景键不受支持。</summary>
    public const string ScenarioUnsupported = "data_approvals.scenario.unsupported";

    /// <summary>审批请求不存在。</summary>
    public const string RequestNotFound = "data_approvals.request.not_found";

    /// <summary>当前状态不允许该操作。</summary>
    public const string StatusInvalid = "data_approvals.status.invalid";

    /// <summary>幂等键无效。</summary>
    public const string IdempotencyKeyInvalid = "data_approvals.idempotency_key.invalid";

    /// <summary>工作流定义未发布或不存在。</summary>
    public const string WorkflowDefinitionMissing = "data_approvals.workflow_definition.missing";

    /// <summary>场景未启用或未配置工作流绑定。</summary>
    public const string ScenarioNotConfigured = "data_approvals.scenario.not_configured";

    /// <summary>场景绑定配置不存在。</summary>
    public const string ScenarioNotFound = "data_approvals.scenario.not_found";

    /// <summary>场景绑定更新冲突。</summary>
    public const string ScenarioConflict = "data_approvals.scenario.conflict";

    /// <summary>无权取消该请求。</summary>
    public const string CancelForbidden = "data_approvals.cancel.forbidden";
}

/// <summary>创建 DataApproval 请求的请求体。</summary>
/// <param name="ScenarioKey">稳定场景键。</param>
/// <param name="TargetEntityId">被变更实体标识。</param>
/// <param name="ProposedChangeJson">提议变更 JSON。</param>
/// <param name="IdempotencyKey">调用方幂等键。</param>
public sealed record CreateDataApprovalRequestBody(
    string ScenarioKey,
    Guid TargetEntityId,
    string ProposedChangeJson,
    string IdempotencyKey);

/// <summary>更新 DataApproval 场景绑定的请求体。</summary>
/// <param name="IsEnabled">是否启用该场景。</param>
/// <param name="WorkflowDefinitionVersionId">绑定的已发布工作流定义版本；启用时必填。</param>
/// <param name="Version">乐观并发版本；首次配置可省略。</param>
public sealed record UpdateDataApprovalScenarioBindingBody(
    bool IsEnabled,
    Guid? WorkflowDefinitionVersionId,
    long? Version);

/// <summary>DataApproval 场景目录与绑定状态的稳定响应。</summary>
public sealed record DataApprovalScenarioResponse(
    string ScenarioKey,
    string ScopeKey,
    bool IsRegistered,
    bool IsEnabled,
    string? WorkflowDefinitionKey,
    Guid? WorkflowDefinitionVersionId,
    long? Version);

/// <summary>取消 DataApproval 请求的请求体。</summary>
/// <param name="IdempotencyKey">调用方幂等键。</param>
public sealed record CancelDataApprovalRequestBody(string IdempotencyKey);

/// <summary>DataApproval 请求的稳定响应。</summary>
public sealed record DataApprovalRequestResponse(
    Guid Id,
    string ScenarioKey,
    Guid TargetEntityId,
    string StatusKey,
    string? BeforeSnapshotJson,
    string AfterSnapshotJson,
    Guid? WorkflowInstanceId,
    long? WorkflowRevision,
    Guid WorkflowDefinitionVersionId,
    Guid SubmittedByUserId,
    DateTimeOffset SubmittedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    long Version);
