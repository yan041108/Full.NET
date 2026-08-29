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

    private static ActOnWorkflowTodoCommand Command(long expectedRevision) =>
        new(TodoId, expectedRevision, new Dictionary<string, JsonElement>(), "同意", "action-001");
}
