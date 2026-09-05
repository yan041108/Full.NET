using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在 Workflow 本地事务中把人工节点激活为单一步骤和一人一票的待办集合。</summary>
/// <param name="commandExecutor">显式 SQL 命令执行器。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
/// <param name="notificationPublisher">事务 Outbox 待办提醒发布器。</param>
internal sealed class WorkflowApprovalActivationWriter(
    ICommandExecutor commandExecutor,
    IIdGenerator idGenerator,
    WorkflowNotificationOutboxPublisher notificationPublisher)
{
    /// <summary>创建审批步骤、审批席位、个人待办和对应可靠提醒。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="tenantScopeKey">可信作用域键。</param>
    /// <param name="nodeKey">稳定审批节点键。</param>
    /// <param name="policy">发布版本固化的多人策略；为空时使用兼容单人。</param>
    /// <param name="fallbackAssigneeUserId">旧定义缺少策略时的办理人。</param>
    /// <param name="executionSequence">实例内严格执行序号。</param>
    /// <param name="arrivedAtUtc">步骤和待办到达时间。</param>
    /// <param name="timeoutPolicy">发布版本固化的超时策略。</param>
    /// <param name="businessType">稳定业务类型。</param>
    /// <param name="businessId">稳定业务标识。</param>
    /// <param name="cancellationToken">取消当前事务写入的令牌。</param>
    /// <returns>新步骤及按策略顺序创建的待办标识。</returns>
    public async Task<WorkflowApprovalActivationResult> WriteAsync(
        Guid instanceId,
        string tenantScopeKey,
        string nodeKey,
        WorkflowApprovalPolicy? policy,
        Guid fallbackAssigneeUserId,
        long executionSequence,
        DateTimeOffset arrivedAtUtc,
        WorkflowTodoTimeoutPolicy? timeoutPolicy,
        string businessType,
        string businessId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantScopeKey);
        var approvers = policy?.ApproverUserIds ?? [fallbackAssigneeUserId];
        var modeKey = policy?.ModeKey ?? "single";
        var requiredApprovals = policy?.RequiredApprovals ?? 1;
        var stepId = idGenerator.NewId();
        var schedule = WorkflowTodoTimeoutSchedule.Create(arrivedAtUtc, timeoutPolicy);

        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertApprovalStep,
            WorkflowSqlParameters.Create(
                ("Id", stepId), ("InstanceId", instanceId), ("NodeKey", nodeKey),
                ("AssignedUserId", approvers.Count == 1 ? approvers[0] : null),
                ("ApprovalModeKey", modeKey), ("RequiredApprovalCount", requiredApprovals),
                ("ApprovalSlotCount", approvers.Count), ("ExecutionSequence", executionSequence),
                ("StartedAtUtc", arrivedAtUtc)),
            cancellationToken).ConfigureAwait(false);

        var todoIds = new List<Guid>(approvers.Count);
        foreach (var approverUserId in approvers)
        {
            var todoId = idGenerator.NewId();
            var slotId = idGenerator.NewId();
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertTodo,
                WorkflowSqlParameters.Create(
                    ("Id", todoId), ("InstanceId", instanceId), ("StepId", stepId),
                    ("AssigneeUserId", approverUserId), ("ArrivedAtUtc", arrivedAtUtc),
                    ("DueAtUtc", schedule.DueAtUtc),
                    ("NextReminderAtUtc", schedule.NextReminderAtUtc),
                    ("EscalateAtUtc", schedule.EscalateAtUtc),
                    ("MaxReminderCount", schedule.MaxReminderCount),
                    ("ReminderIntervalMinutes", schedule.ReminderIntervalMinutes),
                    ("EscalationRecipientUserId", schedule.EscalationRecipientUserId),
                    ("NextTimeoutSignalAtUtc", schedule.NextTimeoutSignalAtUtc)),
                cancellationToken).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertApprovalSlot,
                WorkflowSqlParameters.Create(
                    ("Id", slotId), ("InstanceId", instanceId), ("StepId", stepId),
                    ("TodoId", todoId), ("AssigneeUserId", approverUserId),
                    ("CreatedAtUtc", arrivedAtUtc)),
                cancellationToken).ConfigureAwait(false);
            await notificationPublisher.PublishTodoAssignedAsync(
                instanceId, todoId, approverUserId, businessType, businessId,
                arrivedAtUtc, cancellationToken).ConfigureAwait(false);
            todoIds.Add(todoId);
        }

        return new WorkflowApprovalActivationResult(stepId, todoIds[0], todoIds);
    }

    /// <summary>在并行分支上下文中创建审批步骤、席位、待办和提醒。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="tenantScopeKey">可信作用域键。</param>
    /// <param name="nodeKey">稳定审批节点键。</param>
    /// <param name="policy">发布版本固化的多人策略；为空时使用兼容单人。</param>
    /// <param name="fallbackAssigneeUserId">旧定义缺少策略时的办理人。</param>
    /// <param name="executionSequence">实例内严格执行序号。</param>
    /// <param name="arrivedAtUtc">步骤和待办到达时间。</param>
    /// <param name="timeoutPolicy">发布版本固化的超时策略。</param>
    /// <param name="businessType">稳定业务类型。</param>
    /// <param name="businessId">稳定业务标识。</param>
    /// <param name="parallelJoinId">所属并行汇合状态标识。</param>
    /// <param name="parallelBranchKey">稳定并行分支键。</param>
    /// <param name="cancellationToken">取消当前事务写入的令牌。</param>
    /// <returns>新步骤及按策略顺序创建的待办标识。</returns>
    public async Task<WorkflowApprovalActivationResult> WriteParallelAsync(
        Guid instanceId,
        string tenantScopeKey,
        string nodeKey,
        WorkflowApprovalPolicy? policy,
        Guid fallbackAssigneeUserId,
        long executionSequence,
        DateTimeOffset arrivedAtUtc,
        WorkflowTodoTimeoutPolicy? timeoutPolicy,
        string businessType,
        string businessId,
        Guid parallelJoinId,
        string parallelBranchKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantScopeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(parallelBranchKey);
        var approvers = policy?.ApproverUserIds ?? [fallbackAssigneeUserId];
        var modeKey = policy?.ModeKey ?? "single";
        var requiredApprovals = policy?.RequiredApprovals ?? 1;
        var stepId = idGenerator.NewId();
        var schedule = WorkflowTodoTimeoutSchedule.Create(arrivedAtUtc, timeoutPolicy);

        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertParallelApprovalStep,
            WorkflowSqlParameters.Create(
                ("Id", stepId), ("InstanceId", instanceId), ("NodeKey", nodeKey),
                ("AssignedUserId", approvers.Count == 1 ? approvers[0] : null),
                ("ApprovalModeKey", modeKey), ("RequiredApprovalCount", requiredApprovals),
                ("ApprovalSlotCount", approvers.Count), ("ExecutionSequence", executionSequence),
                ("ParallelJoinId", parallelJoinId), ("ParallelBranchKey", parallelBranchKey),
                ("StartedAtUtc", arrivedAtUtc)),
            cancellationToken).ConfigureAwait(false);

        var todoIds = new List<Guid>(approvers.Count);
        foreach (var approverUserId in approvers)
        {
            var todoId = idGenerator.NewId();
            var slotId = idGenerator.NewId();
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertTodo,
                WorkflowSqlParameters.Create(
                    ("Id", todoId), ("InstanceId", instanceId), ("StepId", stepId),
                    ("AssigneeUserId", approverUserId), ("ArrivedAtUtc", arrivedAtUtc),
                    ("DueAtUtc", schedule.DueAtUtc),
                    ("NextReminderAtUtc", schedule.NextReminderAtUtc),
                    ("EscalateAtUtc", schedule.EscalateAtUtc),
                    ("MaxReminderCount", schedule.MaxReminderCount),
                    ("ReminderIntervalMinutes", schedule.ReminderIntervalMinutes),
                    ("EscalationRecipientUserId", schedule.EscalationRecipientUserId),
                    ("NextTimeoutSignalAtUtc", schedule.NextTimeoutSignalAtUtc)),
                cancellationToken).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertApprovalSlot,
                WorkflowSqlParameters.Create(
                    ("Id", slotId), ("InstanceId", instanceId), ("StepId", stepId),
                    ("TodoId", todoId), ("AssigneeUserId", approverUserId),
                    ("CreatedAtUtc", arrivedAtUtc)),
                cancellationToken).ConfigureAwait(false);
            await notificationPublisher.PublishTodoAssignedAsync(
                instanceId, todoId, approverUserId, businessType, businessId,
                arrivedAtUtc, cancellationToken).ConfigureAwait(false);
            todoIds.Add(todoId);
        }

        return new WorkflowApprovalActivationResult(stepId, todoIds[0], todoIds);
    }
}

/// <summary>描述审批节点激活后创建的步骤和待办集合。</summary>
/// <param name="StepId">新审批步骤标识。</param>
/// <param name="FirstTodoId">为兼容实例响应而返回的首个待办标识。</param>
/// <param name="TodoIds">按审批策略办理人顺序创建的全部待办标识。</param>
internal sealed record WorkflowApprovalActivationResult(
    Guid StepId,
    Guid FirstTodoId,
    IReadOnlyList<Guid> TodoIds);
