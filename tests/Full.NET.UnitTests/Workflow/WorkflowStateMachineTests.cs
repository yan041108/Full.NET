using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowStateMachineTests
{
    private static readonly Guid AssigneeId = Guid.Parse("0198f5d1-9cf7-7a32-aaf6-47425d1252e0");
    private static readonly Guid TodoId = Guid.Parse("0198f5d1-9cf7-7a32-aaf6-47425d1252e1");

    [TestMethod]
    public void Start_creates_running_instance_with_active_todo()
    {
        var result = WorkflowStateMachine.Start(
            new StartWorkflowCommand(
                Guid.Parse("0198f5d1-9cf7-7a32-aaf6-47425d1252e2"),
                "purchase-order",
                "PO-001",
                new Dictionary<string, JsonElement>(),
                "start-001"),
            AssigneeId,
            TodoId);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(WorkflowInstanceStatus.Running, result.State!.InstanceStatus);
        Assert.AreEqual(WorkflowTodoStatus.Active, result.State.TodoStatus);
        Assert.AreEqual(1, result.State.Revision);
    }

    [TestMethod]
    public void Approve_requires_own_active_todo()
    {
        var foreign = WorkflowStateMachine.Approve(
            WorkflowRuntimeState.Active(TodoId, AssigneeId, 3),
            Command(3),
            Guid.Parse("0198f5d1-a4a5-7c94-9f27-1e04eb755025"));
        var inactive = WorkflowStateMachine.Approve(
            WorkflowRuntimeState.Active(TodoId, AssigneeId, 3) with { TodoStatus = WorkflowTodoStatus.Completed },
            Command(3),
            AssigneeId);

        Assert.AreEqual(WorkflowErrorCodes.TodoAssigneeMismatch, foreign.ErrorCode);
        Assert.AreEqual(WorkflowErrorCodes.TodoNotActive, inactive.ErrorCode);
    }

    [TestMethod]
    public void Act_rejects_terminal_instance_and_stale_revision()
    {
        var terminal = WorkflowStateMachine.Approve(
            WorkflowRuntimeState.Active(TodoId, AssigneeId, 3) with { InstanceStatus = WorkflowInstanceStatus.Completed },
            Command(3),
            AssigneeId);
        var stale = WorkflowStateMachine.Approve(
            WorkflowRuntimeState.Active(TodoId, AssigneeId, 3),
            Command(2),
            AssigneeId);

        Assert.AreEqual(WorkflowErrorCodes.InstanceTerminal, terminal.ErrorCode);
        Assert.AreEqual(WorkflowErrorCodes.InstanceVersionConflict, stale.ErrorCode);
    }

    [TestMethod]
    public void Repeated_idempotency_key_returns_original_receipt_without_advancing_revision()
    {
        var initial = WorkflowRuntimeState.Active(TodoId, AssigneeId, 3);
        var command = Command(3);
        var first = WorkflowStateMachine.Approve(initial, command, AssigneeId);
        var replay = WorkflowStateMachine.Approve(first.State!, command with { ExpectedRevision = 4 }, AssigneeId);

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(replay.IsSuccess);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(first.Receipt, replay.Receipt);
        Assert.AreEqual(first.State!.Revision, replay.State!.Revision);
    }

    [TestMethod]
    public void Reject_moves_instance_to_rejected_terminal_state()
    {
        var result = WorkflowStateMachine.Reject(
            WorkflowRuntimeState.Active(TodoId, AssigneeId, 7),
            Command(7),
            AssigneeId);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(WorkflowInstanceStatus.Rejected, result.State!.InstanceStatus);
        Assert.AreEqual(WorkflowTodoStatus.Completed, result.State.TodoStatus);
    }

    [TestMethod]
    public void Cancel_moves_running_instance_and_active_todo_to_cancelled()
    {
        var result = WorkflowStateMachine.Cancel(
            WorkflowRuntimeState.Active(TodoId, AssigneeId, 7),
            new CancelWorkflowInstanceCommand(7, "业务申请已撤回", "cancel-001"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(WorkflowInstanceStatus.Cancelled, result.State!.InstanceStatus);
        Assert.AreEqual(WorkflowTodoStatus.Cancelled, result.State.TodoStatus);
        Assert.AreEqual(8, result.State.Revision);
        Assert.AreEqual("cancel", result.Receipt!.Action);
    }

    [TestMethod]
    public void Cancel_replays_same_idempotency_key_and_rejects_stale_or_terminal_instance()
    {
        var initial = WorkflowRuntimeState.Active(TodoId, AssigneeId, 3);
        var command = new CancelWorkflowInstanceCommand(3, "不再需要", "cancel-001");
        var first = WorkflowStateMachine.Cancel(initial, command);
        var replay = WorkflowStateMachine.Cancel(first.State!, command);
        var stale = WorkflowStateMachine.Cancel(
            initial,
            command with { ExpectedRevision = 2, IdempotencyKey = "cancel-002" });
        var terminal = WorkflowStateMachine.Cancel(
            initial with { InstanceStatus = WorkflowInstanceStatus.Completed },
            command with { IdempotencyKey = "cancel-003" });

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(replay.IsSuccess);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(first.Receipt, replay.Receipt);
        Assert.AreEqual(WorkflowErrorCodes.InstanceVersionConflict, stale.ErrorCode);
        Assert.AreEqual(WorkflowErrorCodes.InstanceTerminal, terminal.ErrorCode);
    }

    private static ActOnWorkflowTodoCommand Command(long expectedRevision) =>
        new(TodoId, expectedRevision, new Dictionary<string, JsonElement>(), "同意", "action-001");
}
