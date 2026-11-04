using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>投票办理与后台截止共用的只读上下文，不依赖当前 HTTP 凭据。</summary>
internal sealed class WorkflowVoteActionReader(IQueryExecutor query, WorkflowBranchStateCoordinator branches)
{
    internal async Task<Result<WorkflowVoteActionContext>> ReadAsync(Guid todoId, Guid actor, WorkflowManagementScope scope, CancellationToken token)
    {
        var todo = await query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
            P(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (todo is null) return ContextFail(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        if (todo.AssigneeUserId != actor) return ContextFail(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        if (todo.NodeTypeKey != WorkflowHumanActivityKinds.Vote) return ContextFail(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
        var instance = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            P(("Id", todo.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.FormVersionId is null) return ContextFail(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        var parameters = P(("VoteId", todo.StepId), ("InstanceId", instance.Id), ("TodoId", todo.Id), ("ActorUserId", actor),
            ("TenantScopeKey", scope.TenantScopeKey), ("DefinitionVersionId", instance.DefinitionVersionId), ("NodeKey", todo.NodeKey));
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, parameters, token).ConfigureAwait(false);
        var round = await query.QuerySingleOrDefaultAsync<WorkflowVoteRoundRecord>(WorkflowVoteSql.FindRound, parameters, token).ConfigureAwait(false);
        var binding = await query.QuerySingleOrDefaultAsync<WorkflowVoteFormBindingRecord>(WorkflowVoteSql.FindFormBinding, parameters, token).ConfigureAwait(false);
        if (asset is null || round is null || binding is null || round.FormBindingId != binding.Id ||
            binding.FormSchemaHash != WorkflowSignatureCoordinator.HashSubmission(binding.FormSchemaJson)) return ContextFail(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        var draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var mainSchema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var ballotSchema = JsonSerializer.Deserialize(binding.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var node = draft?.Nodes.SingleOrDefault(node => node.NodeKey == todo.NodeKey);
        if (draft is null || mainSchema is null || ballotSchema is null || node is null || !WorkflowVotePolicy.TryRead(node.Config, out var policy) ||
            policy!.BallotFormVersionId != binding.BallotFormVersionId || !WorkflowRuntimePlan.TryCreate(draft, mainSchema, out var runtime) ||
            !WorkflowHumanActionPolicy.TryResolve(asset.CanonicalJson, todo.NodeKey, out var actions)) return ContextFail(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        var ballot = await query.QuerySingleOrDefaultAsync<WorkflowVoteBallotRecord>(WorkflowVoteSql.FindBallot, parameters, token).ConfigureAwait(false);
        var submission = await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance, parameters, token).ConfigureAwait(false);
        submission = await branches.ReadAsync(todo, submission, scope.TenantScopeKey, token).ConfigureAwait(false);
        if (submission is null) return ContextFail(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        return Result<WorkflowVoteActionContext>.Success(new(todo, instance, asset, round, binding, ballot, policy, mainSchema, ballotSchema, runtime!, actions!, submission));
    }

    private static object P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<WorkflowVoteActionContext> ContextFail(string code, ErrorType type) => Result<WorkflowVoteActionContext>.Failure(new(code, "The private ballot is unavailable.", type));
}
