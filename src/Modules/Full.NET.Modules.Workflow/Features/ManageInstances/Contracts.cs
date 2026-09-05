using System.Text.Json;

namespace Full.NET.Modules.Workflow.Features.ManageInstances;

internal sealed record StartWorkflowInstanceRequest(
    Guid DefinitionVersionId,
    string BusinessType,
    string BusinessId,
    JsonElement InitialValues,
    string IdempotencyKey);

internal sealed record CancelWorkflowInstanceRequest(
    long ExpectedRevision,
    string? Reason,
    string IdempotencyKey);

/// <summary>暂停工作流实例请求。</summary>
/// <param name="ExpectedRevision">客户端最后读取到的实例修订号。</param>
/// <param name="Reason">可选暂停原因，最多 512 个字符。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
internal sealed record PauseWorkflowInstanceRequest(
    long ExpectedRevision,
    string? Reason,
    string IdempotencyKey);

/// <summary>普通恢复已暂停工作流实例请求。</summary>
/// <param name="ExpectedRevision">客户端最后读取到的实例修订号。</param>
/// <param name="Reason">可选恢复原因，最多 512 个字符。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
internal sealed record ResumeWorkflowInstanceRequest(
    long ExpectedRevision,
    string? Reason,
    string IdempotencyKey);

/// <summary>管理员强制恢复已暂停工作流实例请求。</summary>
/// <param name="ExpectedRevision">客户端最后读取到的实例修订号。</param>
/// <param name="Reason">强制恢复原因，规范化后不得为空。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
internal sealed record RecoverWorkflowInstanceRequest(
    long ExpectedRevision,
    string Reason,
    string IdempotencyKey);

/// <summary>活动工作流待办改派请求。</summary>
/// <param name="AssigneeUserId">当前可信作用域内的新办理人标识。</param>
/// <param name="ExpectedRevision">客户端最后读取到的实例修订号。</param>
/// <param name="Reason">可选的改派原因，最多 512 个字符。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
internal sealed record ReassignWorkflowInstanceRequest(
    Guid AssigneeUserId,
    long ExpectedRevision,
    string? Reason,
    string IdempotencyKey);

internal sealed record WorkflowInstanceResponse(
    Guid Id,
    Guid DefinitionVersionId,
    Guid FormVersionId,
    string BusinessType,
    string BusinessId,
    string StatusKey,
    long Revision,
    Guid? ActiveTodoId,
    DateTimeOffset StartedAtUtc);

internal sealed record WorkflowExecutionLogResponse(
    Guid Id,
    Guid InstanceId,
    Guid? StepId,
    string TransitionKey,
    string? FromStatusKey,
    string ToStatusKey,
    DateTimeOffset CreatedAtUtc);
