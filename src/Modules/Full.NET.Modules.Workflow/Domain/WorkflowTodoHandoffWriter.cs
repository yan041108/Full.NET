using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>各显式授权入口在实例锁内共用不可变交接；这里只写本模块事实，不调用身份目录。</summary>
internal sealed class WorkflowTodoHandoffWriter(IQueryExecutor query, ICommandExecutor command, IIdGenerator ids,
    WorkflowHumanActivityActivationWriter activation, WorkflowNotificationOutboxPublisher notifications)
{
    public async Task<Result<WorkflowTodoHandoffPlan>> ResolveAsync(WorkflowTodoRuntimeRecord todo, WorkflowInstanceRecord instance,
        WorkflowManagementScope scope, Guid actor, Guid target, WorkflowTodoHandoffAuthority authority, CancellationToken token)
    {
        if (todo.InstanceId != instance.Id || instance.TenantScopeKey != scope.TenantScopeKey || actor == Guid.Empty || target == Guid.Empty ||
            todo.StatusKey != "active" || instance.StatusKey != "active" || !WorkflowHumanActivityKinds.IsFormActivity(todo.NodeTypeKey) ||
            !Enum.IsDefined(authority)) return Conflict();
        if (authority is WorkflowTodoHandoffAuthority.PersonalTransfer or WorkflowTodoHandoffAuthority.Delegation && todo.AssigneeUserId != actor)
            return Fail(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        var delegation = await query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForProxy,
            P(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (authority == WorkflowTodoHandoffAuthority.DelegationRecall)
        {
            if (delegation is null || delegation.ProxyTodoId != todo.Id || delegation.ProxyUserId != todo.AssigneeUserId ||
                delegation.OriginalAssigneeUserId != actor || target != actor)
                return Fail(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        }
        else if (delegation is not null && authority != WorkflowTodoHandoffAuthority.AdministrativeReassignment)
            return Fail(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
        if (todo.AssigneeUserId == target) return Fail(WorkflowErrorCodes.TodoAssigneeUnchanged, ErrorType.Validation);
        if (await query.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(WorkflowSql.FindCountersignItemByTodoId,
                P(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null ||
            await query.QuerySingleOrDefaultAsync<WorkflowCountersignChainRecord>(WorkflowSql.FindActiveCountersignChainByOriginTodo,
                P(("OriginTodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null)
            return Fail(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        var lineage = await query.QuerySingleOrDefaultAsync<WorkflowTodoTransferLineageRecord>(WorkflowTodoTransferSql.FindLineage,
            P(("TodoId", todo.Id), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        // 代理预留一次收回的深度预算，不因最后一次授权耗尽预算而锁住原办理人。
        var maximum = authority == WorkflowTodoHandoffAuthority.PersonalTransfer ? 8 : authority == WorkflowTodoHandoffAuthority.Delegation ? 31 : 32;
        if (lineage is { Depth: < 1 } || (lineage is not null && lineage.Depth >= maximum) || lineage?.OriginTodoId == Guid.Empty || lineage?.OriginalAssigneeUserId == Guid.Empty)
            return Fail(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        var origin = lineage?.OriginTodoId ?? todo.Id; var original = lineage?.OriginalAssigneeUserId ?? todo.AssigneeUserId;
        // 管理员可明确纠正先前改派，个人转办仍禁止回到链中旧身份；各入口均有总深度上限。
        if (authority == WorkflowTodoHandoffAuthority.PersonalTransfer && (target == original ||
            await query.QuerySingleOrDefaultAsync<int>(WorkflowTodoTransferSql.CountPreviousActor,
                P(("OriginTodoId", origin), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                    ("AssigneeUserId", target)), token).ConfigureAwait(false) != 0))
            return Fail(WorkflowErrorCodes.TodoAssigneeUnchanged, ErrorType.Validation);
        WorkflowTaskContextRecord? member = null;
        WorkflowApprovalSlotRecord? slot = null;
        WorkflowVoteBallotRecord? ballot = null;
        WorkflowClaimAttemptRecord? claimAttempt = null;
        WorkflowClaimPoolRecord? claimPool = null;
        DateTimeOffset? voteClosesAt = null;
        if (todo.NodeTypeKey == WorkflowHumanActivityKinds.Claim)
        {
            var parameters = P(("PoolId", todo.StepId), ("InstanceId", instance.Id), ("TodoId", todo.Id),
                ("ActorUserId", todo.AssigneeUserId), ("TenantScopeKey", scope.TenantScopeKey));
            claimPool = await query.QuerySingleOrDefaultAsync<WorkflowClaimPoolRecord>(WorkflowClaimSql.FindPool, parameters, token).ConfigureAwait(false);
            claimAttempt = await query.QuerySingleOrDefaultAsync<WorkflowClaimAttemptRecord>(WorkflowClaimSql.FindAttemptForTodo, parameters, token).ConfigureAwait(false);
            if (claimPool?.StatusKey != "claimed" || claimPool.CurrentTodoId != todo.Id || claimPool.CurrentAssigneeUserId != todo.AssigneeUserId ||
                claimAttempt?.StatusKey != "active" || claimAttempt.ClaimCycle != claimPool.ClaimCycle) return Conflict();
        }
        else if (todo.NodeTypeKey == WorkflowHumanActivityKinds.Vote)
        {
            var parameters = P(("VoteId", todo.StepId), ("InstanceId", instance.Id), ("TodoId", todo.Id),
                ("ActorUserId", todo.AssigneeUserId), ("TenantScopeKey", scope.TenantScopeKey));
            var round = await query.QuerySingleOrDefaultAsync<WorkflowVoteRoundRecord>(WorkflowVoteSql.FindRound, parameters, token).ConfigureAwait(false);
            ballot = await query.QuerySingleOrDefaultAsync<WorkflowVoteBallotRecord>(WorkflowVoteSql.FindBallot, parameters, token).ConfigureAwait(false);
            if (round?.StatusKey != "active" || ballot is null || ballot.StatusKey != "pending" || ballot.VoteId != todo.StepId ||
                ballot.TodoId != todo.Id || ballot.AssigneeUserId != todo.AssigneeUserId) return Conflict();
            voteClosesAt = round.ClosesAtUtc;
            // 包含已投成员，改派不能为同一人重新提供第二张票，也不改变候选分母。
            if (await query.QuerySingleOrDefaultAsync<int>(WorkflowVoteSql.CountOtherMember,
                P(("VoteId", todo.StepId), ("InstanceId", instance.Id), ("ExcludedBallotId", ballot.Id),
                    ("AssigneeUserId", target), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 0)
                return Fail(WorkflowErrorCodes.TodoAssigneeUnchanged, ErrorType.Validation);
        }
        else if (todo.NodeTypeKey == WorkflowHumanActivityKinds.Task)
        {
            member = await query.QuerySingleOrDefaultAsync<WorkflowTaskContextRecord>(WorkflowTaskSql.FindForTodo,
                P(("TodoId", todo.Id), ("TaskId", todo.StepId), ("InstanceId", instance.Id), ("ActorUserId", todo.AssigneeUserId),
                    ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (member is null) return Conflict();
            if (await query.QuerySingleOrDefaultAsync<int>(WorkflowTaskSql.CountOtherMember,
                P(("TaskId", todo.StepId), ("InstanceId", instance.Id), ("ExcludedMemberId", member.Id),
                    ("AssigneeUserId", target), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 0)
                return Fail(WorkflowErrorCodes.TodoAssigneeUnchanged, ErrorType.Validation);
        }
        else
        {
            slot = await query.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(WorkflowSql.FindApprovalSlotForReassignment,
                P(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (slot is null && todo.ApprovalModeKey is not (null or "single")) return Conflict();
            if (slot is not null && await query.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(WorkflowSql.FindApprovalSlotByStepAssignee,
                P(("StepId", todo.StepId), ("AssigneeUserId", target), ("ExcludedSlotId", slot.Id),
                    ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null)
                return Fail(WorkflowErrorCodes.TodoAssigneeUnchanged, ErrorType.Validation);
        }
        return Result<WorkflowTodoHandoffPlan>.Success(new(todo.Id, instance.Id, todo.AssigneeUserId, actor, target,
            authority, origin, original, (lineage?.Depth ?? 0) + 1, slot, member, ballot, voteClosesAt, claimAttempt, claimPool));
    }

    public async Task<Result<Guid>> WriteAsync(WorkflowTodoHandoffPlan plan, WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance, WorkflowManagementScope scope, IReadOnlyDictionary<string, JsonElement> values,
        long formRevision, DateTimeOffset now, CancellationToken token)
    {
        if (plan.TodoId != todo.Id || plan.InstanceId != instance.Id || plan.FromUserId != todo.AssigneeUserId || formRevision < 1 ||
            instance.TenantScopeKey != scope.TenantScopeKey) return WriteConflict();
        if (todo.NodeTypeKey == WorkflowHumanActivityKinds.Vote && (plan.VoteBallot is null ||
            plan.ApprovalSlot is not null || plan.TaskMember is not null || plan.VoteClosesAtUtc <= now)) return WriteConflict();
        if (todo.NodeTypeKey == WorkflowHumanActivityKinds.Claim && (plan.ClaimAttempt is null || plan.ClaimPool is null ||
            plan.ApprovalSlot is not null || plan.TaskMember is not null || plan.VoteBallot is not null)) return WriteConflict();
        var action = ActionKey(plan.Authority);
        if (await command.ExecuteAsync(WorkflowSql.CompleteTodoWithRevision,
            P(("Id", todo.Id), ("AssigneeUserId", plan.FromUserId), ("TenantScopeKey", scope.TenantScopeKey),
                ("CompletedAtUtc", now), ("ResultActionKey", action), ("Revision", todo.Revision)), token).ConfigureAwait(false) != 1) return WriteConflict();
        var next = ids.NewId();
        if (await command.ExecuteAsync(WorkflowTodoTransferSql.InsertTodo,
            P(("Id", next), ("TodoId", todo.Id), ("InstanceId", instance.Id), ("StepId", todo.StepId), ("AssigneeUserId", plan.ToUserId),
                ("ActorUserId", plan.FromUserId), ("TenantScopeKey", scope.TenantScopeKey), ("Now", now), ("FromActionKey", action)), token).ConfigureAwait(false) != 1)
            return WriteConflict();
        if (plan.ApprovalSlot is { } slot && await command.ExecuteAsync(WorkflowTodoTransferSql.RebindSlot,
            P(("Id", slot.Id), ("InstanceId", instance.Id), ("StepId", todo.StepId), ("TodoId", todo.Id), ("NewTodoId", next),
                ("AssigneeUserId", plan.ToUserId), ("ActorUserId", plan.FromUserId), ("TenantScopeKey", scope.TenantScopeKey), ("Revision", slot.Revision)), token).ConfigureAwait(false) != 1)
            return WriteConflict();
        if (plan.TaskMember is { } member && await command.ExecuteAsync(WorkflowTaskSql.TransferMember,
            P(("Id", member.Id), ("TaskId", todo.StepId), ("InstanceId", instance.Id), ("TodoId", todo.Id), ("NewTodoId", next),
                ("AssigneeUserId", plan.ToUserId), ("ActorUserId", plan.FromUserId), ("TenantScopeKey", scope.TenantScopeKey), ("Revision", member.Revision)), token).ConfigureAwait(false) != 1)
            return WriteConflict();
        if (plan.VoteBallot is { } ballot)
        {
            if (await command.ExecuteAsync(WorkflowVoteSql.TransferBallot,
                P(("Id", ballot.Id), ("VoteId", todo.StepId), ("InstanceId", instance.Id), ("TodoId", todo.Id), ("NewTodoId", next),
                    ("AssigneeUserId", plan.ToUserId), ("ActorUserId", plan.FromUserId), ("TenantScopeKey", scope.TenantScopeKey),
                    ("Revision", ballot.Revision)), token).ConfigureAwait(false) != 1 ||
                await command.ExecuteAsync(WorkflowVoteSql.CaptureView,
                P(("TodoId", next), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 1)
                return WriteConflict();
        }
        if (plan.ClaimAttempt is { } attempt && plan.ClaimPool is { } pool)
        {
            var parameters = P(("Id", attempt.Id), ("PoolId", pool.Id), ("InstanceId", instance.Id), ("TodoId", todo.Id),
                ("NewTodoId", next), ("AssigneeUserId", plan.ToUserId), ("ActorUserId", plan.FromUserId),
                ("TenantScopeKey", scope.TenantScopeKey), ("Revision", attempt.Revision));
            if (await command.ExecuteAsync(WorkflowClaimSql.RebindAttempt, parameters, token).ConfigureAwait(false) != 1 ||
                await command.ExecuteAsync(WorkflowClaimSql.RebindPool,
                    P(("PoolId", pool.Id), ("InstanceId", instance.Id), ("TodoId", todo.Id), ("NewTodoId", next),
                        ("AssigneeUserId", plan.ToUserId), ("ActorUserId", plan.FromUserId), ("TenantScopeKey", scope.TenantScopeKey),
                        ("Revision", pool.Revision)), token).ConfigureAwait(false) != 1) return WriteConflict();
        }
        if (await command.ExecuteAsync(WorkflowTodoTransferSql.AdvanceStep,
            P(("InstanceId", instance.Id), ("StepId", todo.StepId), ("ActorUserId", plan.FromUserId), ("AssigneeUserId", plan.ToUserId),
                ("TenantScopeKey", scope.TenantScopeKey), ("Revision", todo.StepRevision)), token).ConfigureAwait(false) != 1 ||
            await command.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,
                P(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey), ("Revision", instance.Revision)), token).ConfigureAwait(false) != 1) return WriteConflict();
        await activation.CaptureInputsAsync(new(todo.StepId, next, [next]), instance.Id, scope.TenantScopeKey, values, token).ConfigureAwait(false);
        if (await command.ExecuteAsync(WorkflowTodoTransferSql.Insert,
            P(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", todo.StepId), ("TenantScopeKey", scope.TenantScopeKey),
                ("SlotId", plan.ApprovalSlot?.Id), ("TaskMemberId", plan.TaskMember?.Id), ("VoteBallotId", plan.VoteBallot?.Id),
                ("ClaimAttemptId", plan.ClaimAttempt?.Id), ("OriginTodoId", plan.OriginTodoId),
                ("FromTodoId", todo.Id), ("ToTodoId", next), ("OriginalAssigneeUserId", plan.OriginalAssigneeUserId),
                ("FromAssigneeUserId", plan.FromUserId), ("ToAssigneeUserId", plan.ToUserId), ("ActorUserId", plan.ActorUserId),
                ("Depth", plan.Depth), ("AuthorizationKey", AuthorizationKey(plan.Authority)), ("FormRevision", formRevision), ("Now", now)), token).ConfigureAwait(false) != 1) return WriteConflict();
        await notifications.PublishTodoAssignedAsync(instance.Id, next, plan.ToUserId, instance.BusinessType, instance.BusinessId, now, token).ConfigureAwait(false);
        return Result<Guid>.Success(next);
    }
    private static object P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    internal static string ActionKey(WorkflowTodoHandoffAuthority authority) => authority switch
    {
        WorkflowTodoHandoffAuthority.PersonalTransfer => "transfer",
        WorkflowTodoHandoffAuthority.Delegation => "delegate",
        WorkflowTodoHandoffAuthority.DelegationRecall => "delegate_recall",
        _ => "reassign",
    };
    internal static string AuthorizationKey(WorkflowTodoHandoffAuthority authority) => authority switch
    {
        WorkflowTodoHandoffAuthority.PersonalTransfer => WorkflowPermissions.TodosTransfer,
        WorkflowTodoHandoffAuthority.Delegation => WorkflowPermissions.TodosDelegate,
        WorkflowTodoHandoffAuthority.DelegationRecall => WorkflowPermissions.TodosRecallDelegation,
        _ => WorkflowPermissions.InstancesRecover,
    };
    private static Result<WorkflowTodoHandoffPlan> Conflict() => Fail(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
    private static Result<WorkflowTodoHandoffPlan> Fail(string code, ErrorType type) => Result<WorkflowTodoHandoffPlan>.Failure(new Error(code, "The todo cannot be handed off.", type));
    private static Result<Guid> WriteConflict() => Result<Guid>.Failure(new Error(WorkflowErrorCodes.RevisionConflict, "The handoff changed.", ErrorType.Conflict));
}

/// <summary>授权来源由调用用例固定，不能由HTTP请求声明。</summary>
internal enum WorkflowTodoHandoffAuthority { PersonalTransfer, AdministrativeReassignment, Delegation, DelegationRecall }
/// <summary>实例锁内验证的交接计划，保留原始成员身份及实际操作者。</summary>
internal sealed record WorkflowTodoHandoffPlan(Guid TodoId, Guid InstanceId, Guid FromUserId, Guid ActorUserId, Guid ToUserId,
    WorkflowTodoHandoffAuthority Authority, Guid OriginTodoId, Guid OriginalAssigneeUserId, int Depth,
    WorkflowApprovalSlotRecord? ApprovalSlot, WorkflowTaskContextRecord? TaskMember,
    WorkflowVoteBallotRecord? VoteBallot = null, DateTimeOffset? VoteClosesAtUtc = null,
    WorkflowClaimAttemptRecord? ClaimAttempt = null, WorkflowClaimPoolRecord? ClaimPool = null);
