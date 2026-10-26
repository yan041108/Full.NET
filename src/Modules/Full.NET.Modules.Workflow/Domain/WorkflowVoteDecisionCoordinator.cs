using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>实例锁内按个人票据汇总，未全部提交且未到可信截止时不写任何最终结果。</summary>
internal sealed class WorkflowVoteDecisionCoordinator(IQueryExecutor query, ICommandExecutor command, IOptions<DatabaseOptions> database)
{
    public async Task<Result<WorkflowVoteDecision>> ResolveAsync(WorkflowVoteRoundRecord round, WorkflowVotePolicy policy,
        WorkflowManagementScope scope, DateTimeOffset now, bool closeForDeadline, CancellationToken token)
    {
        if (round.Id == Guid.Empty || round.InstanceId == Guid.Empty || round.FormBindingId == Guid.Empty ||
            round.MemberCount is < 1 or > 20 || round.StatusKey != "active" || round.Revision < 1 ||
            round.ParticipationPercentage != policy.ParticipationPercentage || round.ApprovalPercentage != policy.ApprovalPercentage ||
            round.AllowAbstain != policy.AllowAbstain || (round.ClosesAtUtc is null) != (policy.CloseAfterSeconds is null) ||
            round.FinalYesCount is not null || round.FinalNoCount is not null || round.FinalAbstainCount is not null || round.FinalUnsubmittedCount is not null)
            return Invalid();
        if (closeForDeadline && (round.ClosesAtUtc is null || round.ClosesAtUtc>now)) return Conflict();
        var counts = await query.QuerySingleOrDefaultAsync<WorkflowVoteCountsRecord>(database.Value.Provider == DatabaseProvider.SqlServer
                ? WorkflowVoteSql.TallySqlServer : WorkflowVoteSql.TallyMySql,
            P(("VoteId", round.Id), ("InstanceId", round.InstanceId)), token).ConfigureAwait(false);
        if (counts is null || counts.YesCount is < 0 or > 20 || counts.NoCount is < 0 or > 20 || counts.AbstainCount is < 0 or > 20 ||
            counts.PendingCount is < 0 or > 20 || counts.CancelledCount != 0 ||
            counts.YesCount+counts.NoCount+counts.AbstainCount+counts.PendingCount != round.MemberCount) return Invalid();
        var outcome = WorkflowVoteRules.Decide(policy, new(round.MemberCount, (int)counts.YesCount, (int)counts.NoCount,
            (int)counts.AbstainCount, (int)counts.PendingCount), closeForDeadline);
        if (outcome == WorkflowVoteOutcome.Invalid) return Invalid();
        if (outcome == WorkflowVoteOutcome.Waiting) return Result<WorkflowVoteDecision>.Success(new(outcome, counts));
        var parameters = P(("VoteId", round.Id), ("InstanceId", round.InstanceId), ("TenantScopeKey", scope.TenantScopeKey),
            ("Revision", round.Revision), ("StatusKey", outcome.ToString().ToLowerInvariant()), ("YesCount", counts.YesCount),
            ("NoCount", counts.NoCount), ("AbstainCount", counts.AbstainCount), ("UnsubmittedCount", counts.PendingCount), ("Now", now));
        if (await command.ExecuteAsync(WorkflowVoteSql.CloseRound, parameters, token).ConfigureAwait(false) != 1) return Conflict();
        await command.ExecuteAsync(WorkflowVoteSql.CancelPendingTodos, parameters, token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowVoteSql.CancelPendingBallots, parameters, token).ConfigureAwait(false);
        return Result<WorkflowVoteDecision>.Success(new(outcome, counts));
    }

    private static object P(params (string Name, object? Value)[] pairs) => WorkflowSqlParameters.Create(pairs);
    private static Result<WorkflowVoteDecision> Invalid() => Result<WorkflowVoteDecision>.Failure(new(
        WorkflowErrorCodes.SchemaInvalid, "The frozen vote state is inconsistent.", ErrorType.BusinessRule));
    private static Result<WorkflowVoteDecision> Conflict() => Result<WorkflowVoteDecision>.Failure(new(
        WorkflowErrorCodes.RevisionConflict, "The vote changed or its deadline has not been reached.", ErrorType.Conflict));
}

/// <summary>额外截止取消数不计参与；最终写入器按该不可变汇总执行结果回填与流程推进。</summary>
internal sealed record WorkflowVoteDecision(WorkflowVoteOutcome Outcome, WorkflowVoteCountsRecord Counts);
