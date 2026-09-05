namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

using System.Text.Json;

internal sealed record ActWorkflowTodoRequest(
    long ExpectedRevision,
    JsonElement FieldPatch,
    string? Comment,
    string IdempotencyKey);

/// <summary>把当前待办退回到指定历史人工审批步骤的请求。</summary>
/// <param name="TargetStepId">服务端合法目标列表中的历史步骤标识。</param>
/// <param name="ExpectedRevision">当前待办期望修订号。</param>
/// <param name="FieldPatch">按当前节点字段策略提交的字段补丁。</param>
/// <param name="Comment">必填退回原因。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
internal sealed record ReturnWorkflowTodoRequest(
    Guid TargetStepId,
    long ExpectedRevision,
    JsonElement FieldPatch,
    string Comment,
    string IdempotencyKey);

/// <summary>待办可退回的历史人工审批步骤。</summary>
/// <param name="StepId">历史步骤标识，提交退回时必须原样回传。</param>
/// <param name="NodeKey">稳定节点键。</param>
/// <param name="AssigneeUserId">该步骤历史办理人快照。</param>
/// <param name="CompletedAtUtc">该步骤完成时间。</param>
internal sealed record WorkflowTodoReturnTargetResponse(
    Guid StepId,
    string NodeKey,
    Guid AssigneeUserId,
    DateTimeOffset CompletedAtUtc);

/// <summary>退回 B0 审计所需的结构化目标信息。</summary>
/// <param name="SourceStepId">发起退回的步骤标识。</param>
/// <param name="TargetStepId">被选择的历史目标步骤标识。</param>
/// <param name="TargetNodeKey">目标稳定节点键。</param>
internal sealed record WorkflowTodoReturnAuditDetail(
    Guid SourceStepId,
    Guid TargetStepId,
    string TargetNodeKey);

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
    long SubmissionRevision);

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
    long SubmissionRevision);
