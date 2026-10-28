using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在 Workflow 本地事务中把人工节点激活为单一步骤和一人一票的待办集合。</summary>
/// <param name="commandExecutor">显式 SQL 命令执行器。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
/// <param name="notificationPublisher">事务 Outbox 待办提醒发布器。</param>
internal sealed partial class WorkflowHumanActivityActivationWriter(
    ICommandExecutor commandExecutor,
    IIdGenerator idGenerator,
    WorkflowNotificationOutboxPublisher notificationPublisher)
{
    /// <summary>活动激活时固定独立截止；转办、改派和个人顺序推进均不重新计时。</summary>
    public async Task WriteSlaAsync(Guid stepId, Guid instanceId, string scope, WorkflowSlaPolicy? policy, DateTimeOffset now, CancellationToken token)
    {
        if (policy is null) return;
        if (await commandExecutor.ExecuteAsync(WorkflowSlaSql.Insert, WorkflowSqlParameters.Create(("Id", stepId),
                ("InstanceId", instanceId), ("TenantScopeKey", scope), ("ActionKey", policy.ActionKey),
                ("PauseModeKey", policy.PauseModeKey), ("DueAtUtc", now.AddMinutes(policy.DueAfterMinutes)), ("Now", now)), token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("workflow.sla.activation_missing");
    }

    /// <summary>办理以独立成员事实激活，顺序模式只创建首人的实际待办。</summary>
    public async Task<WorkflowHumanActivityActivationResult> WriteTaskAsync(Guid instanceId, string tenantScopeKey, Guid executionEpochId, string nodeKey,
        WorkflowTaskActivationPolicy activation, long executionSequence, DateTimeOffset now, WorkflowTodoTimeoutPolicy? timeout,
        string businessType, string businessId, Guid? parallelJoinId, string? branchKey, string? snapshotJson, string? snapshotHash,
        CancellationToken token, WorkflowActivationTrace? trace=null)
    {
        var members = activation.MemberUserIds;
        var required = activation.Policy.RequiredCompletions(members.Count);
        if (required == 0 || members.Distinct().Count() != members.Count || members.Contains(Guid.Empty) ||
            snapshotJson is null || snapshotHash is null || string.IsNullOrWhiteSpace(tenantScopeKey))
            throw new InvalidOperationException("workflow.task.activation_invalid");
        var step = idGenerator.NewId();
        if (await commandExecutor.ExecuteAsync(WorkflowTaskSql.InsertStep,
            WorkflowSqlParameters.Create(("Id", step), ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId), ("NodeKey", nodeKey),
                ("AssignedUserId", members.Count == 1 ? members[0] : null), ("ExecutionSequence", executionSequence),
                ("ParallelJoinId", parallelJoinId), ("ParallelBranchKey", branchKey), ("Now", now),
                ("AssigneeSnapshotJson", snapshotJson), ("AssigneeSnapshotHash", snapshotHash)), token).ConfigureAwait(false) != 1 ||
            await commandExecutor.ExecuteAsync(WorkflowTaskSql.InsertTask,
                WorkflowSqlParameters.Create(("Id", step), ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId), ("CompletionModeKey", activation.Policy.CompletionModeKey),
                    ("MemberCount", members.Count), ("RequiredCompletionCount", required), ("Now", now)), token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("workflow.task.step_missing");
        await WorkflowActivationBindingWriter.BindAsync(commandExecutor,instanceId,tenantScopeKey,executionEpochId,step,now,token,trace,idGenerator).ConfigureAwait(false);
        var schedule = WorkflowTodoTimeoutSchedule.Create(now, timeout);
        var todos = new List<Guid>();
        for (var index = 0; index < members.Count; index++)
        {
            Guid? todo = activation.Policy.CompletionModeKey != "sequential" || index == 0 ? idGenerator.NewId() : null;
            if (todo is { } assigned && await InsertTodoAsync(assigned, instanceId, step, members[index], now, schedule, token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("workflow.task.todo_missing");
            if (await commandExecutor.ExecuteAsync(WorkflowTaskSql.InsertMember,
                WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instanceId), ("TaskId", step),
                    ("TodoId", todo), ("AssigneeUserId", members[index]), ("SequenceNo", index + 1), ("Now", now)), token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("workflow.task.member_missing");
            if (todo is not { } actual) continue;
            todos.Add(actual);
            await notificationPublisher.PublishTodoAssignedAsync(instanceId, actual, members[index], businessType, businessId, now, token).ConfigureAwait(false);
        }
        return new(step, todos[0], todos);
    }

    /// <summary>待办和输入在同一 Workflow 事务创建；并行输入由统一执行器传入实际分支视图。</summary>
    public async Task CaptureInputsAsync(WorkflowHumanActivityActivationResult activation, Guid instanceId, string tenantScopeKey,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> values, CancellationToken token)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            Serialization.WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        foreach (var todoId in activation.TodoIds)
        {
            if (await commandExecutor.ExecuteAsync(WorkflowTodoViewSql.Capture,
                WorkflowSqlParameters.Create(("Id", todoId), ("InstanceId", instanceId), ("TenantScopeKey", tenantScopeKey),
                    ("InputJson", json)), token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("workflow.todo_view.input_missing");
        }
    }

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
    public async Task<WorkflowHumanActivityActivationResult> WriteAsync(
        Guid instanceId,
        string tenantScopeKey, Guid executionEpochId,
        string nodeKey,
        WorkflowApprovalPolicy? policy,
        Guid fallbackAssigneeUserId,
        long executionSequence,
        DateTimeOffset arrivedAtUtc,
        WorkflowTodoTimeoutPolicy? timeoutPolicy,
        string businessType,
        string businessId,
        CancellationToken cancellationToken,
        string? assigneeSnapshotJson = null,
        string? assigneeSnapshotHash = null, WorkflowActivationTrace? trace=null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantScopeKey);
        if (policy?.Groups is not null)
            return await WriteGroupedAsync(instanceId, tenantScopeKey, executionEpochId, nodeKey, policy, executionSequence, arrivedAtUtc,
                timeoutPolicy, businessType, businessId, null, null, assigneeSnapshotJson, assigneeSnapshotHash, cancellationToken, trace).ConfigureAwait(false);
        var approvers = policy?.ApproverUserIds ?? [fallbackAssigneeUserId];
        var modeKey = policy?.ModeKey ?? "single";
        var requiredApprovals = policy?.RequiredApprovals ?? 1;
        var stepId = idGenerator.NewId();
        var schedule = WorkflowTodoTimeoutSchedule.Create(arrivedAtUtc, timeoutPolicy);

        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertApprovalStep,
            WorkflowSqlParameters.Create(
                ("Id", stepId), ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId), ("NodeKey", nodeKey),
                ("AssignedUserId", approvers.Count == 1 ? approvers[0] : null),
                ("ApprovalModeKey", modeKey), ("RequiredApprovalCount", requiredApprovals),
                ("ApprovalSlotCount", approvers.Count), ("ExecutionSequence", executionSequence),
                ("StartedAtUtc", arrivedAtUtc), ("AssigneeSnapshotJson", assigneeSnapshotJson), ("AssigneeSnapshotHash", assigneeSnapshotHash)),
            cancellationToken).ConfigureAwait(false);

        await WorkflowActivationBindingWriter.BindAsync(commandExecutor,instanceId,tenantScopeKey,executionEpochId,stepId,arrivedAtUtc,cancellationToken,trace,idGenerator).ConfigureAwait(false);
        var todoIds = await WriteSlotsAsync(instanceId, stepId, modeKey, approvers, arrivedAtUtc, schedule,
            businessType, businessId, cancellationToken).ConfigureAwait(false);
        return new WorkflowHumanActivityActivationResult(stepId, todoIds[0], todoIds);
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
    public async Task<WorkflowHumanActivityActivationResult> WriteParallelAsync(
        Guid instanceId,
        string tenantScopeKey, Guid executionEpochId,
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
        CancellationToken cancellationToken,
        string? assigneeSnapshotJson = null,
        string? assigneeSnapshotHash = null, WorkflowActivationTrace? trace=null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantScopeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(parallelBranchKey);
        if (policy?.Groups is not null)
            return await WriteGroupedAsync(instanceId, tenantScopeKey, executionEpochId, nodeKey, policy, executionSequence, arrivedAtUtc,
                timeoutPolicy, businessType, businessId, parallelJoinId, parallelBranchKey, assigneeSnapshotJson, assigneeSnapshotHash, cancellationToken, trace).ConfigureAwait(false);
        var approvers = policy?.ApproverUserIds ?? [fallbackAssigneeUserId];
        var modeKey = policy?.ModeKey ?? "single";
        var requiredApprovals = policy?.RequiredApprovals ?? 1;
        var stepId = idGenerator.NewId();
        var schedule = WorkflowTodoTimeoutSchedule.Create(arrivedAtUtc, timeoutPolicy);

        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertParallelApprovalStep,
            WorkflowSqlParameters.Create(
                ("Id", stepId), ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId), ("NodeKey", nodeKey),
                ("AssignedUserId", approvers.Count == 1 ? approvers[0] : null),
                ("ApprovalModeKey", modeKey), ("RequiredApprovalCount", requiredApprovals),
                ("ApprovalSlotCount", approvers.Count), ("ExecutionSequence", executionSequence),
                ("ParallelJoinId", parallelJoinId), ("ParallelBranchKey", parallelBranchKey),
                ("StartedAtUtc", arrivedAtUtc), ("AssigneeSnapshotJson", assigneeSnapshotJson), ("AssigneeSnapshotHash", assigneeSnapshotHash)),
            cancellationToken).ConfigureAwait(false);

        await WorkflowActivationBindingWriter.BindAsync(commandExecutor,instanceId,tenantScopeKey,executionEpochId,stepId,arrivedAtUtc,cancellationToken,trace,idGenerator).ConfigureAwait(false);
        var todoIds = await WriteSlotsAsync(instanceId, stepId, modeKey, approvers, arrivedAtUtc, schedule,
            businessType, businessId, cancellationToken).ConfigureAwait(false);
        return new WorkflowHumanActivityActivationResult(stepId, todoIds[0], todoIds);
    }
    /// <summary>一组一条权威事实；所有成员冻结，但每个依次组只建立首人的实际待办。</summary>
    private async Task<WorkflowHumanActivityActivationResult> WriteGroupedAsync(Guid instanceId, string tenantScopeKey, Guid executionEpochId, string nodeKey,
        WorkflowApprovalPolicy policy, long executionSequence, DateTimeOffset now, WorkflowTodoTimeoutPolicy? timeoutPolicy,
        string businessType, string businessId, Guid? parallelJoinId, string? parallelBranchKey,
        string? snapshotJson, string? snapshotHash, CancellationToken token, WorkflowActivationTrace? trace=null)
    {
        var groups = policy.Groups!;
        var members = groups.SelectMany(group => group.Policy.ApproverUserIds).ToArray();
        if (policy.ModeKey != "group" || groups.Count is < 2 or > 8 || policy.RequiredApprovals < 1 || policy.RequiredApprovals > groups.Count ||
            members.Length is < 1 or > 20 || members.Distinct().Count() != members.Length || members.Contains(Guid.Empty) ||
            !members.SequenceEqual(policy.ApproverUserIds) || snapshotJson is null || snapshotHash is null ||
            groups.Count(group => group.Handling is not null) >= policy.RequiredApprovals ||
            groups.Any(group => group.Handling is { } handled
                ? handled.ActionKey is not ("auto_pass" or "skip") || group.Policy.ModeKey != "system" || group.Policy.ApproverUserIds.Count != 0 || group.Policy.RequiredApprovals != 0
                : group.Policy.ResolveRequiredCount(group.Policy.ApproverUserIds.Count) == 0 || group.Policy.ResolveRequiredCount(group.Policy.ApproverUserIds.Count) != group.Policy.RequiredApprovals))
            throw new InvalidOperationException("workflow.approval_group.activation_invalid");
        var stepId = idGenerator.NewId();
        if (await commandExecutor.ExecuteAsync(WorkflowGroupApprovalSql.InsertStep,
            WorkflowSqlParameters.Create(("Id", stepId), ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId), ("NodeKey", nodeKey),
                ("ExecutionSequence", executionSequence), ("RequiredApprovalCount", policy.RequiredApprovals),
                ("ApprovalSlotCount", members.Length), ("ApprovalGroupCount", groups.Count), ("ParallelJoinId", parallelJoinId),
                ("ParallelBranchKey", parallelBranchKey), ("StartedAtUtc", now), ("AssigneeSnapshotJson", snapshotJson),
                ("AssigneeSnapshotHash", snapshotHash)), token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("workflow.approval_group.step_missing");
        await WorkflowActivationBindingWriter.BindAsync(commandExecutor,instanceId,tenantScopeKey,executionEpochId,stepId,now,token,trace,idGenerator).ConfigureAwait(false);
        var schedule = WorkflowTodoTimeoutSchedule.Create(now, timeoutPolicy);
        var todos = new List<Guid>(); var memberSequence = 0;
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index]; var groupId = idGenerator.NewId();
            if (group.Handling is not null)
            {
                if (await commandExecutor.ExecuteAsync(WorkflowAssigneeHandlingSql.InsertApprovedGroup,
                    WorkflowSqlParameters.Create(("Id", groupId), ("InstanceId", instanceId), ("StepId", stepId),
                        ("TenantScopeKey", tenantScopeKey), ("GroupKey", group.GroupKey), ("GroupName", group.GroupName),
                        ("SequenceNo", index + 1), ("Now", now)), token).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("workflow.assignee_handling.group_missing");
                continue;
            }
            if (await commandExecutor.ExecuteAsync(WorkflowGroupApprovalSql.InsertGroup,
                WorkflowSqlParameters.Create(("Id", groupId), ("InstanceId", instanceId), ("StepId", stepId),
                    ("GroupKey", group.GroupKey), ("GroupName", group.GroupName), ("SequenceNo", index + 1),
                    ("ModeKey", group.Policy.ModeKey), ("RequiredApprovalCount", group.Policy.RequiredApprovals),
                    ("SlotCount", group.Policy.ApproverUserIds.Count), ("Now", now)), token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("workflow.approval_group.group_missing");
            for (var memberIndex = 0; memberIndex < group.Policy.ApproverUserIds.Count; memberIndex++)
            {
                memberSequence++;
                var assignee = group.Policy.ApproverUserIds[memberIndex];
                Guid? todoId = group.Policy.ModeKey != "sequential" || memberIndex == 0 ? idGenerator.NewId() : null;
                if (todoId is { } activeTodo && await InsertTodoAsync(activeTodo, instanceId, stepId, assignee, now, schedule, token).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("workflow.approval_group.todo_missing");
                if (await commandExecutor.ExecuteAsync(WorkflowGroupApprovalSql.InsertSlot,
                    WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instanceId), ("StepId", stepId),
                        ("GroupId", groupId), ("TodoId", todoId), ("AssigneeUserId", assignee),
                        ("SequenceNo", group.Policy.ModeKey == "sequential" ? memberSequence : null), ("Now", now)), token).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("workflow.approval_group.slot_missing");
                if (todoId is not { } assignedTodo) continue;
                todos.Add(assignedTodo);
                await notificationPublisher.PublishTodoAssignedAsync(instanceId, assignedTodo, assignee, businessType, businessId, now, token).ConfigureAwait(false);
            }
        }
        return new(stepId, todos[0], todos);
    }

    /// <summary>冻结全部席位；依次模式只为首席建立实际待办，其他模式保持同时激活。</summary>
    private async Task<IReadOnlyList<Guid>> WriteSlotsAsync(Guid instanceId, Guid stepId, string modeKey,
        IReadOnlyList<Guid> approvers, DateTimeOffset now, WorkflowTodoTimeoutSchedule schedule,
        string businessType, string businessId, CancellationToken token)
    {
        var todos = new List<Guid>();
        for (var index = 0; index < approvers.Count; index++)
        {
            Guid? todoId = modeKey != "sequential" || index == 0 ? idGenerator.NewId() : null;
            if (todoId is { } activeId)
                if (await InsertTodoAsync(activeId, instanceId, stepId, approvers[index], now, schedule, token).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("workflow.todo.activation_missing");
            if (await commandExecutor.ExecuteAsync(modeKey == "sequential" ? WorkflowSequentialApprovalSql.InsertSlot : WorkflowSql.InsertApprovalSlot,
                WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instanceId), ("StepId", stepId),
                    ("TodoId", todoId), ("AssigneeUserId", approvers[index]), ("SequenceNo", index+1), ("CreatedAtUtc", now)), token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("workflow.approval_slot.activation_missing");
            if (todoId is not { } assignedId) continue;
            await notificationPublisher.PublishTodoAssignedAsync(instanceId, assignedId, approvers[index], businessType, businessId, now, token).ConfigureAwait(false);
            todos.Add(assignedId);
        }
        return todos;
    }

    /// <summary>只激活已固定的下一个席位；CAS 失败由调用方失败结果回滚整个办理事务。</summary>
    public async Task<Result<Guid>> ActivateSequentialAsync(WorkflowPendingApprovalSlotRecord slot, WorkflowTodoRuntimeRecord previous,
        WorkflowInstanceRecord instance, string tenantScopeKey, WorkflowTodoTimeoutPolicy? timeoutPolicy,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> values, DateTimeOffset now, CancellationToken token)
    {
        if (previous.InstanceId != instance.Id || previous.ApprovalModeKey != "sequential" || slot.AssigneeUserId == Guid.Empty || slot.SequenceNo is < 2 or > 20)
            return SequentialConflict();
        return await ActivatePendingAsync(new(slot.Id, slot.AssigneeUserId, slot.SequenceNo, slot.Revision), previous, instance, tenantScopeKey, timeoutPolicy, values, now,
            WorkflowSequentialApprovalSql.BindTodo, null, token).ConfigureAwait(false);
    }

    /// <summary>调用方已持有实例锁并确认组内没有其他活动待办，只激活该组固定的下一席。</summary>
    public async Task<Result<Guid>> ActivateGroupedSequentialAsync(WorkflowPendingGroupApprovalSlotRecord slot, WorkflowTodoRuntimeRecord previous,
        WorkflowInstanceRecord instance, string tenantScopeKey, WorkflowTodoTimeoutPolicy? timeoutPolicy,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> values, DateTimeOffset now, CancellationToken token)
    {
        if (previous.InstanceId != instance.Id || previous.ApprovalModeKey != "group" || slot.GroupId == Guid.Empty ||
            slot.AssigneeUserId == Guid.Empty || slot.SequenceNo is < 2 or > 20) return SequentialConflict();
        return await ActivatePendingAsync(new(slot.Id, slot.AssigneeUserId, slot.SequenceNo, slot.Revision), previous,
            instance, tenantScopeKey, timeoutPolicy, values, now, WorkflowGroupApprovalSql.BindTodo, slot.GroupId, token).ConfigureAwait(false);
    }

    /// <summary>顺序办理创建新的个人输入身份，已完成成员不会再次占位。</summary>
    public Task<Result<Guid>> ActivateTaskSequentialAsync(WorkflowPendingTaskMemberRecord member, WorkflowTodoRuntimeRecord previous,
        WorkflowInstanceRecord instance, string tenantScopeKey, WorkflowTodoTimeoutPolicy? timeout,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> values, DateTimeOffset now, CancellationToken token)
    {
        if (previous.InstanceId != instance.Id || previous.NodeTypeKey != WorkflowHumanActivityKinds.Task ||
            member.AssigneeUserId == Guid.Empty || member.SequenceNo is < 2 or > 20) return Task.FromResult(SequentialConflict());
        return ActivatePendingAsync(new(member.Id, member.AssigneeUserId, member.SequenceNo, member.Revision), previous,
            instance, tenantScopeKey, timeout, values, now, WorkflowTaskSql.BindTodo, null, token);
    }

    /// <summary>普通依次和组内依次共用新待办、不可变输入及可靠提醒写入，不重建历史席位。</summary>
    private async Task<Result<Guid>> ActivatePendingAsync(WorkflowPendingHumanAssignment slot, WorkflowTodoRuntimeRecord previous,
        WorkflowInstanceRecord instance, string tenantScopeKey, WorkflowTodoTimeoutPolicy? timeoutPolicy,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> values, DateTimeOffset now, SqlStatement binding,
        Guid? groupId, CancellationToken token)
    {
        var todoId = idGenerator.NewId();
        if (await InsertTodoAsync(todoId, instance.Id, previous.StepId, slot.AssigneeUserId, now,
            WorkflowTodoTimeoutSchedule.Create(now, timeoutPolicy), token).ConfigureAwait(false) != 1)
            return SequentialConflict();
        if (await commandExecutor.ExecuteAsync(binding,
            WorkflowSqlParameters.Create(("Id", slot.Id), ("InstanceId", instance.Id), ("StepId", previous.StepId),
                ("TenantScopeKey", tenantScopeKey), ("AssigneeUserId", slot.AssigneeUserId), ("SequenceNo", slot.SequenceNo),
                ("Revision", slot.Revision), ("TodoId", todoId), ("GroupId", groupId), ("TaskId", previous.StepId)), token).ConfigureAwait(false) != 1)
            return SequentialConflict();
        await CaptureInputsAsync(new(previous.StepId, todoId, [todoId]), instance.Id, tenantScopeKey, values, token).ConfigureAwait(false);
        await notificationPublisher.PublishTodoAssignedAsync(instance.Id, todoId, slot.AssigneeUserId,
            instance.BusinessType, instance.BusinessId, now, token).ConfigureAwait(false);
        return Result<Guid>.Success(todoId);
    }

    private Task<int> InsertTodoAsync(Guid id, Guid instanceId, Guid stepId, Guid userId, DateTimeOffset now,
        WorkflowTodoTimeoutSchedule schedule, CancellationToken token) => commandExecutor.ExecuteAsync(WorkflowSql.InsertTodo,
            WorkflowSqlParameters.Create(("Id", id), ("InstanceId", instanceId), ("StepId", stepId), ("AssigneeUserId", userId),
                ("ArrivedAtUtc", now), ("DueAtUtc", schedule.DueAtUtc), ("NextReminderAtUtc", schedule.NextReminderAtUtc),
                ("EscalateAtUtc", schedule.EscalateAtUtc), ("MaxReminderCount", schedule.MaxReminderCount),
                ("ReminderIntervalMinutes", schedule.ReminderIntervalMinutes), ("EscalationRecipientUserId", schedule.EscalationRecipientUserId),
                ("NextTimeoutSignalAtUtc", schedule.NextTimeoutSignalAtUtc)), token);

    private static Result<Guid> SequentialConflict() => Result<Guid>.Failure(new Error(
        WorkflowErrorCodes.RevisionConflict, "The sequential approval slot changed.", ErrorType.Conflict));
}

/// <summary>描述审批节点激活后创建的步骤和待办集合。</summary>
/// <param name="StepId">新审批步骤标识。</param>
/// <param name="FirstTodoId">为兼容实例响应而返回的首个待办标识。</param>
/// <param name="TodoIds">按审批策略办理人顺序创建的全部待办标识。</param>
internal sealed record WorkflowHumanActivityActivationResult(
    Guid StepId,
    Guid FirstTodoId,
    IReadOnlyList<Guid> TodoIds);

/// <summary>只用于共用的新待办绑定过程，不携带任何审批或办理结果语义。</summary>
internal sealed record WorkflowPendingHumanAssignment(Guid Id, Guid AssigneeUserId, int SequenceNo, long Revision);
