using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>投票为每个实际候选创建独立票据和个人视图，不能借用审批席位承载投票表单。</summary>
internal sealed partial class WorkflowHumanActivityActivationWriter
{
    public async Task<WorkflowHumanActivityActivationResult> WriteVoteAsync(Guid instanceId, string tenantScopeKey, Guid executionEpochId, string nodeKey,
        WorkflowVoteActivationPolicy activation, WorkflowVoteFormBindingRecord binding, long executionSequence, DateTimeOffset now,
        WorkflowTodoTimeoutPolicy? timeout, string businessType, string businessId, Guid? parallelJoinId, string? branchKey,
        string? snapshotJson, string? snapshotHash, CancellationToken token, WorkflowActivationTrace? trace=null)
    {
        var members = activation.MemberUserIds;
        if (members.Count is < 1 or > 20 || members.Distinct().Count() != members.Count || members.Contains(Guid.Empty) ||
            binding.Id == Guid.Empty || binding.BallotFormVersionId != activation.Policy.BallotFormVersionId ||
            snapshotJson is null || snapshotHash is null || string.IsNullOrWhiteSpace(tenantScopeKey))
            throw new InvalidOperationException("workflow.vote.activation_invalid");
        var step = idGenerator.NewId();
        var closes = activation.Policy.CloseAfterSeconds is { } seconds ? now.AddSeconds(seconds) : (DateTimeOffset?)null;
        if (await commandExecutor.ExecuteAsync(WorkflowVoteSql.InsertStep,
                WorkflowSqlParameters.Create(("Id", step), ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId), ("NodeKey", nodeKey), ("ClosesAtUtc", closes),
                    ("ExecutionSequence", executionSequence), ("ParallelJoinId", parallelJoinId), ("ParallelBranchKey", branchKey),
                    ("Now", now), ("AssigneeSnapshotJson", snapshotJson), ("AssigneeSnapshotHash", snapshotHash)), token).ConfigureAwait(false) != 1 ||
            await commandExecutor.ExecuteAsync(WorkflowVoteSql.InsertRound,
                WorkflowSqlParameters.Create(("Id", step), ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId), ("TenantScopeKey", tenantScopeKey),
                    ("FormBindingId", binding.Id), ("MemberCount", members.Count),
                    ("ParticipationPercentage", activation.Policy.ParticipationPercentage), ("ApprovalPercentage", activation.Policy.ApprovalPercentage),
                    ("AllowAbstain", activation.Policy.AllowAbstain), ("ClosesAtUtc", closes), ("Now", now)), token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("workflow.vote.step_missing");
        await WorkflowActivationBindingWriter.BindAsync(commandExecutor,instanceId,tenantScopeKey,executionEpochId,step,now,token,trace,idGenerator).ConfigureAwait(false);
        var schedule = WorkflowTodoTimeoutSchedule.Create(now, timeout);
        var todos = new List<Guid>();
        foreach (var member in members)
        {
            var todo = idGenerator.NewId();
            if (await InsertTodoAsync(todo, instanceId, step, member, now, schedule, token).ConfigureAwait(false) != 1 ||
                await commandExecutor.ExecuteAsync(WorkflowVoteSql.InsertBallot,
                    WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("VoteId", step), ("InstanceId", instanceId),
                        ("TenantScopeKey", tenantScopeKey), ("TodoId", todo), ("AssigneeUserId", member), ("InputJson", "{}"), ("Now", now)), token).ConfigureAwait(false) != 1 ||
                await commandExecutor.ExecuteAsync(WorkflowVoteSql.CaptureView,
                    WorkflowSqlParameters.Create(("TodoId", todo), ("InstanceId", instanceId), ("TenantScopeKey", tenantScopeKey)), token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("workflow.vote.ballot_missing");
            await notificationPublisher.PublishTodoAssignedAsync(instanceId, todo, member, businessType, businessId, now, token).ConfigureAwait(false);
            todos.Add(todo);
        }
        return new(step, todos[0], todos);
    }
}
