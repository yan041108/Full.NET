using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

internal sealed partial class WorkflowHumanActivityActivationWriter
{
    /// <summary>零席位组由系统策略裁定；组通过数不伪造为个人同意票，审计仍绑定真实活动步骤。</summary>
    public async Task RecordGroupHandlingAsync(WorkflowInstanceRecord instance, WorkflowManagementScope scope, Guid stepId,
        IReadOnlyList<WorkflowApprovalGroupAssignees> groups, string snapshotJson, DateTimeOffset now, CancellationToken token)
    {
        var correlation = Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
        foreach (var group in groups.Where(group => group.Handling is not null))
            await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
                WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instance.Id), ("StepId", stepId),
                    ("CorrelationJson", correlation), ("TransitionKey", "assignee.group.system_pass"),
                    ("FromStatusKey", null), ("ToStatusKey", "approved"), ("IdempotencyKey", null),
                    ("Summary", $"group:{group.GroupKey};trigger:{group.Handling!.TriggerKey};action:{group.Handling!.ActionKey}"), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("TenantId", scope.TenantId), ("ScopeKey", scope.ScopeKey),
                ("InstanceId", instance.Id), ("OperationKey", "assignee.handle"), ("ActorUserId", null),
                ("ResourceTypeKey", "step"), ("ResourceId", stepId), ("OutcomeKey", "succeeded"),
                ("DetailJson", snapshotJson), ("CorrelationJson", correlation), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
    }

    /// <summary>系统裁定有独立执行轨迹和审计；Actor 留空，不能冒充发起人执行人工意见。</summary>
    public async Task<Guid> WriteHandlingAsync(WorkflowInstanceRecord instance, WorkflowManagementScope scope, string nodeKey,
        string nodeTypeKey, WorkflowHumanActivityAssignees assignees, long sequence, DateTimeOffset now,
        Guid? joinId, string? branchKey, CancellationToken token)
    {
        if (assignees.Handling is not { } handling || assignees.ResolutionJson is null || assignees.ResolutionHash is null)
            throw new InvalidOperationException("workflow.assignee_handling.snapshot_missing");
        var stepId = idGenerator.NewId();
        var transition = $"assignee.{handling.TriggerKey}.{handling.ActionKey}";
        if (await commandExecutor.ExecuteAsync(WorkflowAssigneeHandlingSql.InsertStep,
            WorkflowSqlParameters.Create(("Id", stepId), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("NodeKey", nodeKey), ("NodeTypeKey", nodeTypeKey), ("ExecutionSequence", sequence),
                ("ParallelJoinId", joinId), ("ParallelBranchKey", branchKey), ("Now", now),
                ("AssigneeSnapshotJson", assignees.ResolutionJson), ("AssigneeSnapshotHash", assignees.ResolutionHash)), token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("workflow.assignee_handling.step_missing");
        var status = handling.ActionKey == "suspend" ? "suspended" : handling.ActionKey == "auto_reject" ? "rejected" : "active";
        var correlation = Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instance.Id), ("StepId", stepId),
                ("CorrelationJson", correlation), ("TransitionKey", transition), ("FromStatusKey", "active"),
                ("ToStatusKey", status), ("IdempotencyKey", null), ("Summary", nodeKey), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("TenantId", scope.TenantId), ("ScopeKey", scope.ScopeKey),
                ("InstanceId", instance.Id), ("OperationKey", "assignee.handle"), ("ActorUserId", null),
                ("ResourceTypeKey", "step"), ("ResourceId", stepId), ("OutcomeKey", "succeeded"),
                ("DetailJson", assignees.ResolutionJson), ("CorrelationJson", correlation), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        return stepId;
    }

    /// <summary>拒绝时在同一本模块事务关闭所有活动前沿；实例状态和拒绝通知由调用入口统一写入。</summary>
    public async Task CancelAfterHandlingAsync(WorkflowInstanceRecord instance, WorkflowManagementScope scope, DateTimeOffset now, Guid? currentWorkId, CancellationToken token)
    {
        await WorkflowHumanActivityLifecycle.CancelAsync(commandExecutor, instance.Id, scope.TenantScopeKey, now, token).ConfigureAwait(false);
        var parameters = WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
            ("Now", now), ("CompletedAtUtc", now), ("ResultActionKey", "cancelled"), ("StatusKey", "cancelled"), ("CurrentWorkId", currentWorkId));
        await commandExecutor.ExecuteAsync(WorkflowActivitySql.CancelApprovalSlots, parameters, token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowAssigneeHandlingSql.CancelOtherWork, parameters, token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.CancelAllActiveTodosByInstance, parameters, token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.CancelAllActiveStepsByInstance, parameters, token).ConfigureAwait(false);
    }
}
