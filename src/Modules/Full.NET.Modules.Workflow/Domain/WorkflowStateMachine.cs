using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>工作流实例的闭合运行状态。</summary>
internal enum WorkflowInstanceStatus
{
    /// <summary>实例正在运行，允许办理、暂停和取消。</summary>
    Running,
    /// <summary>实例已暂停，保留原活动节点且禁止推进。</summary>
    Suspended,
    /// <summary>实例已完成，属于终态。</summary>
    Completed,
    /// <summary>实例已驳回，属于终态。</summary>
    Rejected,
    /// <summary>实例已取消，属于终态。</summary>
    Cancelled,
}

/// <summary>工作流待办的闭合办理状态。</summary>
internal enum WorkflowTodoStatus
{
    /// <summary>待办仍可由当前办理人处理。</summary>
    Active,
    /// <summary>待办已办理完成。</summary>
    Completed,
    /// <summary>待办已随实例取消而关闭。</summary>
    Cancelled,
}

internal sealed record WorkflowActionReceipt(string IdempotencyKey, string Action, long Revision);

internal sealed record WorkflowRuntimeState(
    WorkflowInstanceStatus InstanceStatus,
    WorkflowTodoStatus TodoStatus,
    Guid TodoId,
    Guid AssigneeUserId,
    long Revision,
    IReadOnlyDictionary<string, WorkflowActionReceipt> Receipts)
{
    public static WorkflowRuntimeState Active(Guid todoId, Guid assigneeUserId, long revision) =>
        new(
            WorkflowInstanceStatus.Running,
            WorkflowTodoStatus.Active,
            todoId,
            assigneeUserId,
            revision,
            new Dictionary<string, WorkflowActionReceipt>(StringComparer.Ordinal));
}

internal sealed record StartWorkflowCommand(
    Guid DefinitionVersionId,
    string BusinessType,
    string BusinessId,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> InitialValues,
    string IdempotencyKey);

internal sealed record ActOnWorkflowTodoCommand(
    Guid TodoId,
    long ExpectedRevision,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> FieldPatch,
    string? Comment,
    string IdempotencyKey);

internal sealed record CancelWorkflowInstanceCommand(
    long ExpectedRevision,
    string? Reason,
    string IdempotencyKey);

/// <summary>暂停正在运行的工作流实例。</summary>
/// <param name="ExpectedRevision">客户端最后读取到的实例修订号。</param>
/// <param name="Reason">可选暂停原因，最多 512 个字符。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
internal sealed record PauseWorkflowInstanceCommand(
    long ExpectedRevision,
    string? Reason,
    string IdempotencyKey);

/// <summary>把已暂停实例恢复到原活动节点继续运行。</summary>
/// <param name="ExpectedRevision">客户端最后读取到的实例修订号。</param>
/// <param name="Reason">可选恢复原因，最多 512 个字符。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
internal sealed record ResumeWorkflowInstanceCommand(
    long ExpectedRevision,
    string? Reason,
    string IdempotencyKey);

/// <summary>管理员强制恢复已暂停实例，必须记录原因。</summary>
/// <param name="ExpectedRevision">客户端最后读取到的实例修订号。</param>
/// <param name="Reason">强制恢复原因，规范化后不得为空。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
internal sealed record RecoverWorkflowInstanceCommand(
    long ExpectedRevision,
    string Reason,
    string IdempotencyKey);

internal sealed record WorkflowTransitionResult(
    bool IsSuccess,
    WorkflowRuntimeState? State,
    WorkflowActionReceipt? Receipt,
    string? ErrorCode,
    bool IsReplay)
{
    public static WorkflowTransitionResult Failure(string errorCode) =>
        new(false, null, null, errorCode, false);
}

/// <summary>工作流运行时状态机；暂停恢复必须保留原待办，终态禁止再转换。</summary>
internal static class WorkflowStateMachine
{
    public static WorkflowTransitionResult Start(
        StartWorkflowCommand command,
        Guid assigneeUserId,
        Guid todoId)
    {
        var receipt = new WorkflowActionReceipt(command.IdempotencyKey, "start", 1);
        var receipts = new Dictionary<string, WorkflowActionReceipt>(StringComparer.Ordinal)
        {
            [command.IdempotencyKey] = receipt,
        };
        var state = new WorkflowRuntimeState(
            WorkflowInstanceStatus.Running,
            WorkflowTodoStatus.Active,
            todoId,
            assigneeUserId,
            1,
            receipts);
        return new WorkflowTransitionResult(true, state, receipt, null, false);
    }

    public static WorkflowTransitionResult Approve(
        WorkflowRuntimeState state,
        ActOnWorkflowTodoCommand command,
        Guid actorUserId) =>
        Act(state, command, actorUserId, "approve", WorkflowInstanceStatus.Completed);

    public static WorkflowTransitionResult Reject(
        WorkflowRuntimeState state,
        ActOnWorkflowTodoCommand command,
        Guid actorUserId) =>
        Act(state, command, actorUserId, "reject", WorkflowInstanceStatus.Rejected);

    /// <summary>取消运行中或已暂停的实例，并关闭当前活动待办。</summary>
    /// <param name="state">当前运行时状态。</param>
    /// <param name="command">取消命令。</param>
    /// <returns>取消后的状态、幂等重放结果或稳定错误。</returns>
    public static WorkflowTransitionResult Cancel(
        WorkflowRuntimeState state,
        CancelWorkflowInstanceCommand command)
    {
        if (state.Receipts.TryGetValue(command.IdempotencyKey, out var existing))
        {
            return existing.Action == "cancel"
                ? new WorkflowTransitionResult(true, state, existing, null, true)
                : WorkflowTransitionResult.Failure(WorkflowErrorCodes.InstanceVersionConflict);
        }

        if (IsTerminal(state.InstanceStatus))
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.InstanceTerminal);
        }

        if (state.InstanceStatus is not (WorkflowInstanceStatus.Running or WorkflowInstanceStatus.Suspended))
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.InvalidTransition);
        }

        if (state.TodoStatus != WorkflowTodoStatus.Active)
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.TodoNotActive);
        }

        if (state.Revision != command.ExpectedRevision)
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.InstanceVersionConflict);
        }

        var nextRevision = state.Revision + 1;
        var receipt = new WorkflowActionReceipt(command.IdempotencyKey, "cancel", nextRevision);
        var receipts = new Dictionary<string, WorkflowActionReceipt>(state.Receipts, StringComparer.Ordinal)
        {
            [command.IdempotencyKey] = receipt,
        };
        return new WorkflowTransitionResult(
            true,
            state with
            {
                InstanceStatus = WorkflowInstanceStatus.Cancelled,
                TodoStatus = WorkflowTodoStatus.Cancelled,
                Revision = nextRevision,
                Receipts = receipts,
            },
            receipt,
            null,
            false);
    }

    /// <summary>把运行中的实例暂停，并保留原活动待办。</summary>
    /// <param name="state">当前运行时状态。</param>
    /// <param name="command">暂停命令。</param>
    /// <returns>暂停后的状态、幂等重放结果或稳定错误。</returns>
    public static WorkflowTransitionResult Pause(
        WorkflowRuntimeState state,
        PauseWorkflowInstanceCommand command) =>
        FreezeOrRelease(
            state,
            command.IdempotencyKey,
            command.ExpectedRevision,
            "pause",
            WorkflowInstanceStatus.Running,
            WorkflowInstanceStatus.Suspended);

    /// <summary>把已暂停实例恢复到原活动节点，不得新建待办。</summary>
    /// <param name="state">当前运行时状态。</param>
    /// <param name="command">普通恢复命令。</param>
    /// <returns>恢复后的状态、幂等重放结果或稳定错误。</returns>
    public static WorkflowTransitionResult Resume(
        WorkflowRuntimeState state,
        ResumeWorkflowInstanceCommand command) =>
        FreezeOrRelease(
            state,
            command.IdempotencyKey,
            command.ExpectedRevision,
            "resume",
            WorkflowInstanceStatus.Suspended,
            WorkflowInstanceStatus.Running);

    /// <summary>管理员强制恢复已暂停实例，动作键与普通恢复区分以便审计。</summary>
    /// <param name="state">当前运行时状态。</param>
    /// <param name="command">强制恢复命令。</param>
    /// <returns>恢复后的状态、幂等重放结果或稳定错误。</returns>
    public static WorkflowTransitionResult Recover(
        WorkflowRuntimeState state,
        RecoverWorkflowInstanceCommand command) =>
        FreezeOrRelease(
            state,
            command.IdempotencyKey,
            command.ExpectedRevision,
            "recover",
            WorkflowInstanceStatus.Suspended,
            WorkflowInstanceStatus.Running);

    /// <summary>办理活动待办；暂停实例必须失败关闭，不得推进自动节点。</summary>
    /// <param name="state">当前运行时状态。</param>
    /// <param name="command">办理命令。</param>
    /// <param name="actorUserId">当前办理人标识。</param>
    /// <param name="action">稳定动作键。</param>
    /// <param name="terminalStatus">成功后的实例终态。</param>
    /// <returns>办理结果或稳定错误。</returns>
    private static WorkflowTransitionResult Act(
        WorkflowRuntimeState state,
        ActOnWorkflowTodoCommand command,
        Guid actorUserId,
        string action,
        WorkflowInstanceStatus terminalStatus)
    {
        if (state.Receipts.TryGetValue(command.IdempotencyKey, out var existing))
        {
            return new WorkflowTransitionResult(true, state, existing, null, true);
        }

        if (state.InstanceStatus == WorkflowInstanceStatus.Suspended)
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.InvalidTransition);
        }

        if (state.InstanceStatus != WorkflowInstanceStatus.Running)
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.InstanceTerminal);
        }

        if (state.TodoStatus != WorkflowTodoStatus.Active)
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.TodoNotActive);
        }

        if (state.TodoId != command.TodoId || state.AssigneeUserId != actorUserId)
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.TodoAssigneeMismatch);
        }

        if (state.Revision != command.ExpectedRevision)
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.InstanceVersionConflict);
        }

        var nextRevision = state.Revision + 1;
        var receipt = new WorkflowActionReceipt(command.IdempotencyKey, action, nextRevision);
        var receipts = new Dictionary<string, WorkflowActionReceipt>(state.Receipts, StringComparer.Ordinal)
        {
            [command.IdempotencyKey] = receipt,
        };
        var nextState = state with
        {
            InstanceStatus = terminalStatus,
            TodoStatus = WorkflowTodoStatus.Completed,
            Revision = nextRevision,
            Receipts = receipts,
        };
        return new WorkflowTransitionResult(true, nextState, receipt, null, false);
    }

    /// <summary>在运行与暂停之间切换实例，且必须保持原待办标识和活动状态。</summary>
    /// <param name="state">当前运行时状态。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <param name="expectedRevision">期望的当前修订号。</param>
    /// <param name="action">稳定动作键。</param>
    /// <param name="requiredStatus">该动作允许的当前实例状态。</param>
    /// <param name="nextStatus">成功后的目标实例状态。</param>
    /// <returns>转换结果；暂停恢复都不得改变待办状态。</returns>
    private static WorkflowTransitionResult FreezeOrRelease(
        WorkflowRuntimeState state,
        string idempotencyKey,
        long expectedRevision,
        string action,
        WorkflowInstanceStatus requiredStatus,
        WorkflowInstanceStatus nextStatus)
    {
        if (state.Receipts.TryGetValue(idempotencyKey, out var existing))
        {
            return existing.Action == action
                ? new WorkflowTransitionResult(true, state, existing, null, true)
                : WorkflowTransitionResult.Failure(WorkflowErrorCodes.InstanceVersionConflict);
        }

        if (IsTerminal(state.InstanceStatus))
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.InstanceTerminal);
        }

        if (state.InstanceStatus != requiredStatus)
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.InvalidTransition);
        }

        if (state.TodoStatus != WorkflowTodoStatus.Active)
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.TodoNotActive);
        }

        if (state.Revision != expectedRevision)
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.InstanceVersionConflict);
        }

        var nextRevision = state.Revision + 1;
        var receipt = new WorkflowActionReceipt(idempotencyKey, action, nextRevision);
        var receipts = new Dictionary<string, WorkflowActionReceipt>(state.Receipts, StringComparer.Ordinal)
        {
            [idempotencyKey] = receipt,
        };
        return new WorkflowTransitionResult(
            true,
            state with
            {
                InstanceStatus = nextStatus,
                Revision = nextRevision,
                Receipts = receipts,
            },
            receipt,
            null,
            false);
    }

    /// <summary>判断实例是否已进入不可再暂停或恢复的终态。</summary>
    /// <param name="status">当前实例状态。</param>
    /// <returns>完成、驳回或取消时返回 <see langword="true"/>。</returns>
    private static bool IsTerminal(WorkflowInstanceStatus status) =>
        status is WorkflowInstanceStatus.Completed
            or WorkflowInstanceStatus.Rejected
            or WorkflowInstanceStatus.Cancelled;
}
