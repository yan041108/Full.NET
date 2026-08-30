using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

internal enum WorkflowInstanceStatus
{
    Running,
    Completed,
    Rejected,
    Cancelled,
}

internal enum WorkflowTodoStatus
{
    Active,
    Completed,
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

        if (state.InstanceStatus != WorkflowInstanceStatus.Running)
        {
            return WorkflowTransitionResult.Failure(WorkflowErrorCodes.InstanceTerminal);
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
}
