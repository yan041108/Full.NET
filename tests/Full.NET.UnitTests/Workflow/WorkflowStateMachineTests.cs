using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>
/// 工作流运行时状态机测试。
/// </summary>
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
            new Full.NET.Modules.Workflow.Domain.CancelWorkflowInstanceCommand(7, "业务申请已撤回", "cancel-001"));

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
        var command = new Full.NET.Modules.Workflow.Domain.CancelWorkflowInstanceCommand(3, "不再需要", "cancel-001");
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

    /// <summary>
    /// 暂停必须保留原活动待办，并对终态或重复暂停失败关闭。
    /// </summary>
    [TestMethod]
    public void Pause_keeps_the_original_active_todo_and_rejects_terminal_or_already_suspended()
    {
        var initial = WorkflowRuntimeState.Active(TodoId, AssigneeId, 3);
        var command = new PauseWorkflowInstanceCommand(3, "等待补充材料", "pause-001");
        var paused = WorkflowStateMachine.Pause(initial, command);
        var replay = WorkflowStateMachine.Pause(paused.State!, command);
        var alreadySuspended = WorkflowStateMachine.Pause(
            paused.State!,
            command with { IdempotencyKey = "pause-002", ExpectedRevision = 4 });
        var terminal = WorkflowStateMachine.Pause(
            initial with { InstanceStatus = WorkflowInstanceStatus.Completed },
            command with { IdempotencyKey = "pause-003" });

        Assert.IsTrue(paused.IsSuccess);
        Assert.AreEqual(WorkflowInstanceStatus.Suspended, paused.State!.InstanceStatus);
        Assert.AreEqual(WorkflowTodoStatus.Active, paused.State.TodoStatus);
        Assert.AreEqual(TodoId, paused.State.TodoId);
        Assert.AreEqual(4, paused.State.Revision);
        Assert.AreEqual("pause", paused.Receipt!.Action);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(paused.Receipt, replay.Receipt);
        Assert.AreEqual(WorkflowErrorCodes.InvalidTransition, alreadySuspended.ErrorCode);
        Assert.AreEqual(WorkflowErrorCodes.InstanceTerminal, terminal.ErrorCode);
    }

    /// <summary>
    /// 普通恢复与强制恢复都从原待办继续，且不得作用于运行中或终态实例。
    /// </summary>
    [TestMethod]
    public void Resume_and_recover_restore_running_without_replacing_the_todo()
    {
        var paused = WorkflowStateMachine.Pause(
            WorkflowRuntimeState.Active(TodoId, AssigneeId, 5),
            new PauseWorkflowInstanceCommand(5, null, "pause-001")).State!;
        var resume = WorkflowStateMachine.Resume(
            paused,
            new ResumeWorkflowInstanceCommand(6, null, "resume-001"));
        var recover = WorkflowStateMachine.Recover(
            paused,
            new RecoverWorkflowInstanceCommand(6, "卡住后由管理员恢复", "recover-001"));
        var runningResume = WorkflowStateMachine.Resume(
            WorkflowRuntimeState.Active(TodoId, AssigneeId, 5),
            new ResumeWorkflowInstanceCommand(5, null, "resume-002"));
        var completedRecover = WorkflowStateMachine.Recover(
            WorkflowRuntimeState.Active(TodoId, AssigneeId, 5) with
            {
                InstanceStatus = WorkflowInstanceStatus.Rejected,
            },
            new RecoverWorkflowInstanceCommand(5, "终态不可恢复", "recover-002"));

        Assert.IsTrue(resume.IsSuccess);
        Assert.AreEqual(WorkflowInstanceStatus.Running, resume.State!.InstanceStatus);
        Assert.AreEqual(TodoId, resume.State.TodoId);
        Assert.AreEqual(WorkflowTodoStatus.Active, resume.State.TodoStatus);
        Assert.AreEqual("resume", resume.Receipt!.Action);
        Assert.IsTrue(recover.IsSuccess);
        Assert.AreEqual(TodoId, recover.State!.TodoId);
        Assert.AreEqual("recover", recover.Receipt!.Action);
        Assert.AreEqual(WorkflowErrorCodes.InvalidTransition, runningResume.ErrorCode);
        Assert.AreEqual(WorkflowErrorCodes.InstanceTerminal, completedRecover.ErrorCode);
    }

    /// <summary>
    /// 暂停实例上的办理必须返回无效转换，而不是终态错误。
    /// </summary>
    [TestMethod]
    public void Approve_rejects_suspended_instance_as_invalid_transition()
    {
        var suspended = WorkflowRuntimeState.Active(TodoId, AssigneeId, 3) with
        {
            InstanceStatus = WorkflowInstanceStatus.Suspended,
        };
        var result = WorkflowStateMachine.Approve(suspended, Command(3), AssigneeId);

        Assert.AreEqual(WorkflowErrorCodes.InvalidTransition, result.ErrorCode);
    }

    /// <summary>
    /// 已暂停实例仍可取消，已完成实例继续按终态拒绝。
    /// </summary>
    [TestMethod]
    public void Cancel_allows_suspended_instance_and_still_rejects_completed()
    {
        var suspended = WorkflowRuntimeState.Active(TodoId, AssigneeId, 4) with
        {
            InstanceStatus = WorkflowInstanceStatus.Suspended,
        };
        var cancelled = WorkflowStateMachine.Cancel(
            suspended,
            new Full.NET.Modules.Workflow.Domain.CancelWorkflowInstanceCommand(4, "暂停后撤回", "cancel-suspend-001"));
        var completed = WorkflowStateMachine.Cancel(
            suspended with { InstanceStatus = WorkflowInstanceStatus.Completed },
            new Full.NET.Modules.Workflow.Domain.CancelWorkflowInstanceCommand(4, "终态", "cancel-suspend-002"));

        Assert.IsTrue(cancelled.IsSuccess);
        Assert.AreEqual(WorkflowInstanceStatus.Cancelled, cancelled.State!.InstanceStatus);
        Assert.AreEqual(WorkflowErrorCodes.InstanceTerminal, completed.ErrorCode);
    }

    private static ActOnWorkflowTodoCommand Command(long expectedRevision) =>
        new(TodoId, expectedRevision, new Dictionary<string, JsonElement>(), "同意", "action-001");
}
