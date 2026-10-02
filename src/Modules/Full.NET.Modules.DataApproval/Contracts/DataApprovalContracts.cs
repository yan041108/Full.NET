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

    /// <summary>人工重试待关联工作流的审批请求。</summary>
    public const string Retry = "data_approvals.requests.retry";

    /// <summary>人工重试批准后业务应用。</summary>
    public const string RetryApply = "data_approvals.requests.retry_apply";

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

    /// <summary>当前恢复状态不允许重试。</summary>
    public const string RecoveryNotRetryable = "data_approvals.recovery.not_retryable";

    /// <summary>恢复重试因并发冲突失败。</summary>
    public const string RecoveryRetryConflict = "data_approvals.recovery.retry_conflict";

    /// <summary>当前应用状态不允许重试。</summary>
    public const string ApplicationNotRetryable = "data_approvals.application.not_retryable";

    /// <summary>应用重试因并发冲突失败。</summary>
    public const string ApplicationRetryConflict = "data_approvals.application.retry_conflict";
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 ScenarioKey、ScopeKey、WorkflowDefinitionKey 等稳定键发布后不可改名或删除。</remarks>
/// <param name="ScenarioKey">稳定场景键；决定审批路由与工作流绑定。</param>
/// <param name="ScopeKey">场景作用域稳定键；决定适用范围。</param>
/// <param name="IsRegistered">场景是否已在代码侧注册；false 表示绑定配置为孤儿。</param>
/// <param name="IsEnabled">是否启用该场景；false 时提交请求将被拒绝。</param>
/// <param name="WorkflowDefinitionKey">绑定的工作流定义稳定键；未绑定时为空。</param>
/// <param name="WorkflowDefinitionVersionId">绑定的工作流定义版本标识；未绑定时为空。</param>
/// <param name="Version">乐观并发版本；首次配置时为空，更新时必须一致。</param>
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

/// <summary>人工重试 DataApproval 工作流关联的请求体。</summary>
/// <param name="Version">乐观并发版本，必须与当前请求一致。</param>
public sealed record RetryDataApprovalRequestBody(long Version);

/// <summary>DataApproval 请求的稳定响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 ScenarioKey、StatusKey、RecoveryStatusKey、ApplicationStatusKey 及 LastFailureCode/LastApplicationFailureCode 等稳定键发布后不可改名或删除。</remarks>
/// <param name="Id">审批请求标识。</param>
/// <param name="ScenarioKey">稳定场景键；决定审批路由。</param>
/// <param name="TargetEntityId">被变更实体标识。</param>
/// <param name="StatusKey">审批状态稳定键；取值见 <see cref="DataApprovalStatusKeys"/>。</param>
/// <param name="BeforeSnapshotJson">变更前快照 JSON；首次提交或未捕获时为空。</param>
/// <param name="AfterSnapshotJson">提议或应用后的快照 JSON；用于人工与自动对账。</param>
/// <param name="WorkflowInstanceId">关联工作流实例标识；尚未关联时为空。</param>
/// <param name="WorkflowRevision">关联工作流修订号；用于幂等校验，未关联时为空。</param>
/// <param name="WorkflowDefinitionVersionId">提交时绑定的工作流定义版本标识。</param>
/// <param name="SubmittedByUserId">提交人用户标识。</param>
/// <param name="SubmittedAtUtc">提交时间（UTC）。</param>
/// <param name="ResolvedAtUtc">审批结论时间（UTC）；未结束时为空。</param>
/// <param name="RecoveryStatusKey">工作流关联恢复状态稳定键；决定是否可重试关联。</param>
/// <param name="LastFailureCode">最近一次恢复失败的稳定错误码；无失败时为空。</param>
/// <param name="LastFailureMessage">最近一次恢复失败的可读说明；不包含敏感数据。</param>
/// <param name="LastRecoveryAttemptAtUtc">最近一次恢复尝试时间（UTC）；从未尝试时为空。</param>
/// <param name="RecoveryAttemptCount">恢复尝试累计次数；用于判断是否触发熔断。</param>
/// <param name="ApplicationStatusKey">业务应用恢复状态稳定键；决定是否可重试应用。</param>
/// <param name="LastApplicationFailureCode">最近一次应用失败的稳定错误码；无失败时为空。</param>
/// <param name="LastApplicationFailureMessage">最近一次应用失败的可读说明；不包含敏感数据。</param>
/// <param name="LastApplicationAttemptAtUtc">最近一次应用尝试时间（UTC）；从未尝试时为空。</param>
/// <param name="ApplicationAttemptCount">应用尝试累计次数；用于判断是否触发熔断。</param>
/// <param name="Version">乐观并发版本；用于更新与重试校验。</param>
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
    string RecoveryStatusKey,
    string? LastFailureCode,
    string? LastFailureMessage,
    DateTimeOffset? LastRecoveryAttemptAtUtc,
    int RecoveryAttemptCount,
    string ApplicationStatusKey,
    string? LastApplicationFailureCode,
    string? LastApplicationFailureMessage,
    DateTimeOffset? LastApplicationAttemptAtUtc,
    int ApplicationAttemptCount,
    long Version);
