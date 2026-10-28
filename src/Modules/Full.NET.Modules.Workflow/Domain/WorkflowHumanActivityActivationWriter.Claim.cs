using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>领取等待点只创建候选池；没有实际办理人时不能伪造Todo或发送已分配事件。</summary>
internal sealed partial class WorkflowHumanActivityActivationWriter
{
    public async Task<Guid> WriteClaimAsync(Guid instanceId, string tenantScopeKey, Guid executionEpochId, string nodeKey,
        WorkflowClaimActivationPolicy activation, long executionSequence, DateTimeOffset now,
        WorkflowTodoTimeoutPolicy? timeout, Guid? parallelJoinId, string? branchKey,
        string? snapshotJson, string? snapshotHash, CancellationToken token, WorkflowActivationTrace? trace=null)
    {
        var members = activation.MemberUserIds;
        if (members.Count is < 1 or > WorkflowClaimPolicy.MaximumCandidates || members.Distinct().Count() != members.Count ||
            members.Contains(Guid.Empty) || snapshotJson is null || snapshotHash is null || string.IsNullOrWhiteSpace(tenantScopeKey))
            throw new InvalidOperationException("workflow.claim.activation_invalid");
        var step = idGenerator.NewId(); var schedule = WorkflowTodoTimeoutSchedule.Create(now, timeout);
        if (await commandExecutor.ExecuteAsync(WorkflowClaimSql.InsertStep,
                WorkflowSqlParameters.Create(("Id", step), ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId), ("NodeKey", nodeKey), ("DueAtUtc", schedule.DueAtUtc),
                    ("ExecutionSequence", executionSequence), ("ParallelJoinId", parallelJoinId), ("ParallelBranchKey", branchKey),
                    ("Now", now), ("AssigneeSnapshotJson", snapshotJson), ("AssigneeSnapshotHash", snapshotHash)), token).ConfigureAwait(false) != 1 ||
            await commandExecutor.ExecuteAsync(WorkflowClaimSql.InsertPool,
                WorkflowSqlParameters.Create(("Id", step), ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId), ("TenantScopeKey", tenantScopeKey),
                    ("AllowRelease", activation.Policy.AllowRelease), ("DueAtUtc", schedule.DueAtUtc),
                    ("NextReminderAtUtc", schedule.NextReminderAtUtc), ("EscalateAtUtc", schedule.EscalateAtUtc),
                    ("MaxReminderCount", schedule.MaxReminderCount), ("ReminderIntervalMinutes", schedule.ReminderIntervalMinutes),
                    ("EscalationRecipientUserId", schedule.EscalationRecipientUserId), ("Now", now)), token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("workflow.claim.pool_missing");
        await WorkflowActivationBindingWriter.BindAsync(commandExecutor,instanceId,tenantScopeKey,executionEpochId,step,now,token,trace,idGenerator).ConfigureAwait(false);
        for (var index = 0; index < members.Count; index++)
            if (await commandExecutor.ExecuteAsync(WorkflowClaimSql.InsertCandidate,
                WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("PoolId", step), ("InstanceId", instanceId),
                    ("TenantScopeKey", tenantScopeKey), ("UserId", members[index]), ("SequenceNo", index + 1)), token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("workflow.claim.candidate_missing");
        return step;
    }
}
