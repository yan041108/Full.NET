// 模块 API 契约类型；与 Endpoint 和 Handler 共享的稳定形状。
namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

using System.Text.Json;

/// <summary>本人采集的签名文件引用；仅在实际办理成功并绑定证据后成为动作事实。</summary>
internal sealed record WorkflowSignatureUploadResponse(Guid FileId, long SizeBytes, string? ContentHash);

/// <summary>本人已办理动作的签名绑定元数据；文件内容必须经同一待办资源授权读取。</summary>
internal sealed record WorkflowSignatureEvidenceResponse(Guid Id, Guid ActorUserId, Guid OriginalAssigneeUserId,
    string ActionKey, Guid FormVersionId, long FormRevision, string SubmissionHash, string FileHash, DateTimeOffset CreatedAtUtc);

internal sealed record ActWorkflowTodoRequest(
    long ExpectedRevision,
    JsonElement FieldPatch,
    string? Comment,
    string IdempotencyKey,
    long ExpectedFormRevision,
    Guid? SignatureFileId = null);

/// <summary>独立选票修订与完整答案；租户及成员身份只来自可信待办上下文。</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
internal sealed record VoteWorkflowTodoRequest(long ExpectedRevision, long ExpectedBallotRevision,
    string ChoiceKey, JsonElement Values, string? Comment, string IdempotencyKey, Guid? SignatureFileId = null);

/// <summary>仅本人本次待办的独立表单，不包含其他候选的答案或可替代身份的票据ID。</summary>
internal sealed record WorkflowVoteFormResponse(Guid FormVersionId, string FormSchemaHash, JsonElement FormSchema,
    JsonElement Values, IReadOnlyDictionary<string, string> FieldPolicies, long Revision, string? ChoiceKey,
    bool ReadOnly, bool AllowAbstain, JsonElement? WebRenderJson);

/// <summary>活动中仅公开提交人数；选择汇总在收敛后才公开，未提交不计参与。</summary>
internal sealed record WorkflowVoteProgressResponse(int MemberCount, int RequiredParticipationCount, int SubmittedCount,
    int PendingCount, int CancelledCount, string StatusKey, DateTimeOffset? ClosesAtUtc,
    int? YesCount, int? NoCount, int? AbstainCount, int? UnsubmittedCount);

/// <summary>本人转办只改变当前席位持有人，提交时不能夹带未保存的表单修改。</summary>
internal sealed record TransferWorkflowTodoRequest(Guid AssigneeUserId, long ExpectedRevision,
    long ExpectedFormRevision, string? Comment, string IdempotencyKey, Guid? SignatureFileId = null);

/// <summary>只授权本人当前待办，不接受租户、原办理人或权限依据等客户端身份声明。</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
internal sealed record DelegateWorkflowTodoRequest(Guid AssigneeUserId, long ExpectedRevision,
    long ExpectedFormRevision, string? Comment, string IdempotencyKey, Guid? SignatureFileId = null);

/// <summary>收回绑定服务端查到的代理待办修订及当前分支表单修订，不接受客户端指定代理目标。</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
internal sealed record RecallWorkflowTodoDelegationRequest(long ExpectedRevision, long ExpectedFormRevision,
    string? Comment, string IdempotencyKey);

/// <summary>本人委托或代理的最小事实；不向委托人泄露代理提交的私有表单内容。</summary>
internal sealed record WorkflowTodoDelegationResponse(Guid OriginTodoId, Guid ProxyTodoId, Guid OriginalAssigneeUserId,
    Guid ProxyUserId, string StatusKey, long ProxyRevision, long FormRevision, bool CanRecall, DateTimeOffset CreatedAtUtc);

/// <summary>未参与代理时返回显式空事实，保持HTTP成功响应与生成客户端的形状一致。</summary>
internal sealed record WorkflowTodoDelegationStateResponse(WorkflowTodoDelegationResponse? Delegation);

/// <summary>记录原始身份、实际执行人及新激活，转办不能伪装为同意票。</summary>
internal sealed record WorkflowTodoTransferAuditDetail(Guid OriginTodoId, Guid FromTodoId, Guid ToTodoId,
    Guid OriginalAssigneeUserId, Guid FromAssigneeUserId, Guid ToAssigneeUserId, Guid ActorUserId,
    int Depth, string AuthorizationKey, long FormRevision);

/// <summary>本人所属步骤的组状态及计数，不公开其他成员的表单、签名或文件。</summary>
internal sealed record WorkflowApprovalGroupProgressResponse(Guid Id, string GroupKey, string? GroupName, int SequenceNo,
    string ApprovalModeKey, int RequiredApprovalCount, int MemberCount, int ApprovedCount, int RejectedCount,
    int PendingCount, int CancelledCount, string StatusKey, bool IsCurrentGroup);

/// <summary>把当前待办退回到指定历史人工审批步骤的请求。</summary>
/// <param name="TargetStepId">服务端合法目标列表中的历史步骤标识。</param>
/// <param name="ExpectedRevision">当前待办期望修订号。</param>
/// <param name="FieldPatch">按当前节点字段策略提交的字段补丁。</param>
/// <param name="Comment">必填退回原因。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
/// <param name="ExpectedFormRevision">办理详情返回的独立表单修订号。</param>
internal sealed record ReturnWorkflowTodoRequest(
    Guid TargetStepId,
    long ExpectedRevision,
    JsonElement FieldPatch,
    string Comment,
    string IdempotencyKey,
    long ExpectedFormRevision,
    Guid? SignatureFileId = null);

/// <summary>待办可退回的历史人工审批步骤。</summary>
/// <param name="StepId">历史步骤标识，提交退回时必须原样回传。</param>
/// <param name="NodeKey">稳定节点键。</param>
/// <param name="AssigneeUserId">单人步骤的历史办理人；多人或分组为空，不能据此决定重新激活成员。</param>
/// <param name="CompletedAtUtc">该步骤完成时间。</param>
internal sealed record WorkflowTodoReturnTargetResponse(
    Guid StepId,
    string NodeKey,
    Guid? AssigneeUserId,
    DateTimeOffset CompletedAtUtc);

/// <summary>退回 B0 审计所需的结构化目标信息。</summary>
/// <param name="SourceStepId">发起退回的步骤标识。</param>
/// <param name="TargetStepId">被选择的历史目标步骤标识。</param>
/// <param name="TargetNodeKey">目标稳定节点键。</param>
internal sealed record WorkflowTodoReturnAuditDetail(
    Guid SourceStepId,
    Guid TargetStepId,
    string TargetNodeKey);

/// <summary>对活动待办发起前加签或后加签的请求。</summary>
/// <param name="DirectionKey">加签方向，before 或 after。</param>
/// <param name="AssigneeUserIds">按顺序排列的加签办理人，最多 10 人。</param>
/// <param name="ExpectedRevision">当前待办期望修订号。</param>
/// <param name="Comment">可选加签说明。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
internal sealed record CountersignWorkflowTodoRequest(
    string DirectionKey,
    IReadOnlyList<Guid> AssigneeUserIds,
    long ExpectedRevision,
    string? Comment,
    string IdempotencyKey);

/// <summary>取消活动加签链的请求。</summary>
/// <param name="ExpectedRevision">当前待办期望修订号。</param>
/// <param name="Comment">可选取消原因。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
internal sealed record CancelWorkflowTodoCountersignRequest(
    long ExpectedRevision,
    string? Comment,
    string IdempotencyKey);

/// <summary>加签链及有序加签项的只读响应。</summary>
internal sealed record WorkflowTodoCountersignChainResponse(
    Guid ChainId,
    string DirectionKey,
    string StatusKey,
    IReadOnlyList<WorkflowTodoCountersignItemResponse> Items);

/// <summary>单个加签项的只读响应。</summary>
internal sealed record WorkflowTodoCountersignItemResponse(
    Guid ItemId,
    int SequenceNo,
    Guid AssigneeUserId,
    string StatusKey,
    Guid? TodoId);

/// <summary>多人审批动作完成后的权威票数快照。</summary>
/// <param name="ApprovalModeKey">单人、会签、或签或 N-of-M 模式键。</param>
/// <param name="RequiredApprovalCount">步骤通过所需的同意票数。</param>
/// <param name="ApprovedCount">当前已同意票数。</param>
/// <param name="RejectedCount">当前已驳回票数。</param>
/// <param name="PendingCount">当前仍待处理票数。</param>
internal sealed record WorkflowApprovalAuditDetail(
    string ApprovalModeKey,
    int RequiredApprovalCount,
    int ApprovedCount,
    int RejectedCount,
    int PendingCount);

internal sealed record WorkflowTodoResponse(
    Guid Id,
    Guid InstanceId,
    Guid StepId,
    Guid AssigneeUserId,
    string StatusKey,
    DateTimeOffset ArrivedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultActionKey,
    long Revision);

/// <summary>待办/已办分页列表项，附带实例上下文与历史动作快照字段。</summary>
/// <param name="Id">待办标识。</param>
/// <param name="InstanceId">流程实例标识。</param>
/// <param name="StepId">流程步骤标识。</param>
/// <param name="StatusKey">待办状态键。</param>
/// <param name="ArrivedAtUtc">到达时间（UTC）。</param>
/// <param name="CompletedAtUtc">完成时间（UTC）；未完成时为空。</param>
/// <param name="ResultActionKey">完成时写入的处理结果动作键快照。</param>
/// <param name="Revision">待办修订号。</param>
/// <param name="BusinessType">关联业务类型。</param>
/// <param name="BusinessId">关联业务标识。</param>
/// <param name="InstanceStatusKey">实例当前状态键。</param>
/// <param name="DefinitionKey">流程定义稳定键。</param>
/// <param name="NodeKey">步骤节点键。</param>
internal sealed record WorkflowTodoListItemResponse(
    Guid Id,
    Guid InstanceId,
    Guid StepId,
    string StatusKey,
    DateTimeOffset ArrivedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultActionKey,
    long Revision,
    string BusinessType,
    string BusinessId,
    string? BusinessTitle,
    string InstanceStatusKey,
    string DefinitionKey,
    string NodeKey);

internal sealed record WorkflowTodoDetailResponse(
    Guid Id,
    Guid InstanceId,
    Guid StepId,
    Guid AssigneeUserId,
    string StatusKey,
    long Revision,
    Guid FormVersionId,
    JsonElement FormSchema,
    JsonElement Submission,
    IReadOnlyDictionary<string, string> FieldPolicies,
    long SubmissionRevision)
{
    /// <summary>分组节点的实际组进度；普通节点为空，组数与成员票数分开显示。</summary>
    /// <summary>固定活动类型决定可执行动作，办理不使用审批计数。</summary>
    public string ActivityKindKey { get; init; } = "human.approval";
    /// <summary>办理完成进度与同意/驳回票独立。</summary>
    public WorkflowTaskProgressResponse? TaskProgress { get; init; }
    /// <summary>投票独立表单及固定分母进度，普通人工活动为空。</summary>
    public WorkflowVoteFormResponse? VoteForm { get; init; }
    public WorkflowVoteProgressResponse? VoteProgress { get; init; }
    public WorkflowClaimProgressResponse? ClaimProgress { get; init; }
    public IReadOnlyList<WorkflowApprovalGroupProgressResponse> ApprovalGroups { get; init; } = [];
    /// <summary>发布版本允许的资源动作；客户端仍须与平台精确权限取交集。</summary>
    public IReadOnlyList<string> AllowedActions { get; init; } = [];
    /// <summary>办理动作是否必须绑定签名文件证据。</summary>
    public bool SignRequired { get; init; }
    /// <summary>办理动作是否必须填写非空意见。</summary>
    public bool CommentRequired { get; init; }
    /// <summary>单人、会签、或签或 N-of-M 模式键。</summary>
    public required string ApprovalModeKey { get; init; }

    /// <summary>步骤通过所需的同意票数。</summary>
    public required int RequiredApprovalCount { get; init; }

    /// <summary>当前已同意票数。</summary>
    public required int ApprovedCount { get; init; }

    /// <summary>当前已驳回票数。</summary>
    public required int RejectedCount { get; init; }

    /// <summary>当前仍待处理票数。</summary>
    public required int PendingCount { get; init; }
}

internal sealed record WorkflowTodoRuntimeResponse(
    Guid Id,
    Guid InstanceId,
    Guid StepId,
    Guid AssigneeUserId,
    string StatusKey,
    long Revision,
    Guid FormVersionId,
    string FormSchemaHash,
    JsonElement FormSchema,
    JsonElement Submission,
    IReadOnlyDictionary<string, string> FieldPolicies,
    long SubmissionRevision)
{
    /// <summary>经过本人待办及可信作用域授权的有界组进度。</summary>
    /// <summary>固定活动类型决定可执行动作，办理不使用审批计数。</summary>
    public string ActivityKindKey { get; init; } = "human.approval";
    /// <summary>办理完成进度与同意/驳回票独立。</summary>
    public WorkflowTaskProgressResponse? TaskProgress { get; init; }
    /// <summary>投票独立表单及固定分母进度，普通人工活动为空。</summary>
    public WorkflowVoteFormResponse? VoteForm { get; init; }
    public WorkflowVoteProgressResponse? VoteProgress { get; init; }
    public WorkflowClaimProgressResponse? ClaimProgress { get; init; }
    public IReadOnlyList<WorkflowApprovalGroupProgressResponse> ApprovalGroups { get; init; } = [];
    /// <summary>发布版本允许的资源动作；不增加当前用户的平台权限。</summary>
    public IReadOnlyList<string> AllowedActions { get; init; } = [];
    /// <summary>办理动作是否必须绑定签名文件证据。</summary>
    public bool SignRequired { get; init; }
    /// <summary>办理动作是否必须填写非空意见。</summary>
    public bool CommentRequired { get; init; }
    /// <summary>经过当前办理人字段授权裁剪的原版 Web 渲染 JSON；不含完整设计资产。</summary>
    public JsonElement? WebRenderJson { get; init; }

    /// <summary>单人、会签、或签或 N-of-M 模式键。</summary>
    public required string ApprovalModeKey { get; init; }

    /// <summary>步骤通过所需的同意票数。</summary>
    public required int RequiredApprovalCount { get; init; }

    /// <summary>当前已同意票数。</summary>
    public required int ApprovedCount { get; init; }

    /// <summary>当前已驳回票数。</summary>
    public required int RejectedCount { get; init; }

    /// <summary>当前仍待处理票数。</summary>
    public required int PendingCount { get; init; }
}

/// <summary>实际固定办理人数及完成进度，不包含审批票数。</summary>
internal sealed record WorkflowTaskProgressResponse(string CompletionModeKey, int MemberCount, int RequiredCompletionCount,
    int CompletedCount, int PendingCount, int CancelledCount);
