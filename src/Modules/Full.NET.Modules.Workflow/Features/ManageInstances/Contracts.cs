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

/// <summary>工作流实例详情与当前活动待办的超时摘要。</summary>
/// <param name="Id">实例标识。</param>
/// <param name="DefinitionVersionId">发布定义版本标识。</param>
/// <param name="FormVersionId">发布表单版本标识。</param>
/// <param name="BusinessType">稳定业务类型。</param>
/// <param name="BusinessId">稳定业务标识。</param>
/// <param name="StatusKey">实例状态机器键。</param>
/// <param name="Revision">实例乐观并发修订号。</param>
/// <param name="ActiveTodoId">当前活动待办标识。</param>
/// <param name="StartedAtUtc">实例发起时间（UTC）。</param>
/// <param name="DueAtUtc">当前活动待办逾期时间（UTC）。</param>
/// <param name="TimeoutStatusKey">超时状态机器键。</param>
/// <param name="ReminderCount">已原子提交的催办次数。</param>
/// <param name="EscalatedAtUtc">升级通知提交时间（UTC）。</param>
internal sealed record WorkflowInstanceResponse(
    Guid Id,
    Guid DefinitionVersionId,
    Guid FormVersionId,
    string BusinessType,
    string BusinessId,
    string StatusKey,
    long Revision,
    Guid? ActiveTodoId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? DueAtUtc = null,
    string TimeoutStatusKey = "not_configured",
    int ReminderCount = 0,
    DateTimeOffset? EscalatedAtUtc = null);

internal sealed record WorkflowExecutionLogResponse(
    Guid Id,
    Guid InstanceId,
    Guid? StepId,
    string TransitionKey,
    string? FromStatusKey,
    string ToStatusKey,
    DateTimeOffset CreatedAtUtc);
