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

/// <summary>投票提交使用独立修订和内容，不将个人答案写入主表单；所有事实沿实例锁一次提交。</summary>
internal sealed class WorkflowVoteTodoService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    ICurrentTenant tenant, IClock clock, IIdGenerator ids, WorkflowVoteAttachmentCoordinator attachments,
    WorkflowSignatureCoordinator signatures, WorkflowHumanActivityPreparation assignees,
    WorkflowVoteDecisionCoordinator decisions, WorkflowVoteResultWriter results, WorkflowBranchStateCoordinator branches)
{
    public async Task<Result<WorkflowInstanceResponse>> VoteAsync(Guid todoId, Guid actor, VoteWorkflowTodoRequest request,
        CancellationToken token = default)
    {
        if (todoId == Guid.Empty || actor == Guid.Empty || request.ExpectedRevision < 1 || request.ExpectedBallotRevision < 1 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128 || request.Comment is { Length: > 512 })
            return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var scope = WorkflowManagementScope.Resolve(tenant);
        var context = await ReadAsync(todoId, actor, scope, token).ConfigureAwait(false);
        if (!context.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(context.Error!);
        var initial = context.Value!;
        if (!WorkflowVoteSubmissionRules.TryCompile(initial.BallotSchema, request.Values, request.ChoiceKey, initial.Policy.AllowAbstain, out var submission))
            return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var hash = WorkflowVoteSubmissionRules.HashRequest(todoId, actor, request.ExpectedRevision, request.ExpectedBallotRevision,
            submission!, request.Comment, request.SignatureFileId);
        var receipt = await ReceiptAsync(initial.Instance.Id, request.IdempotencyKey, token).ConfigureAwait(false);
        if (receipt is not null) return Replay(initial, actor, hash, receipt);
        var active = ValidateActive(initial, request, clock.UtcNow);
        if (!active.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(active.Error!);
        var permitted = initial.Actions.Validate("vote", request.Comment, request.SignatureFileId);
        if (!permitted.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(permitted.Error!);

        // 预备下一人只读身份目录；最终票仍复核实际主表单修订，部分票不争用主表单修订。
        var nextValues = WorkflowVoteResultWriter.ResultValues(initial, true);
        if (nextValues is null || !initial.RuntimePlan.TryResolveAfterHuman(initial.Todo.NodeKey, nextValues,
            initial.Todo.ParallelJoinNodeKey, out var transition)) return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        var preparedAssignees = await assignees.PrepareAsync(transition, scope, initial.Instance.StartedById, token, nextValues, initial.Instance.Id, initial.Todo.NodeKey,
            flowAdministratorUserIds: initial.RuntimePlan.FlowAdministratorUserIds, executionEpochId: await WorkflowExecutionEpochScope.ReadForStepAsync(query, initial.Instance.Id, scope.TenantScopeKey, initial.Todo.StepId, token).ConfigureAwait(false)).ConfigureAwait(false);
        var files = await attachments.PrepareAsync(initial.Ballot!, initial.Instance.Id, actor, scope.TenantScopeKey,
            initial.BallotSchema, request.Values, submission!.EvidenceHash, token).ConfigureAwait(false);
        if (!files.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(files.Error!);
        WorkflowPreparedSignature? signature = null;
        try
        {
            var prepared = await signatures.PrepareForFormAsync(initial.Todo, initial.Instance, actor, "vote", request.IdempotencyKey,
                request.SignatureFileId, initial.Binding.BallotFormVersionId, request.ExpectedBallotRevision + 1,
                submission.EvidenceJson, token).ConfigureAwait(false);
            if (!prepared.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(prepared.Error!);
            signature = prepared.Value;
            return await transaction.ExecuteResultAsync(async inner =>
            {
                if (await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction, P(("Id", initial.Instance.Id),
                    ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false) != 1) return Conflict();
                var reread = await ReadAsync(todoId, actor, scope, inner).ConfigureAwait(false);
                if (!reread.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(reread.Error!);
                var current = reread.Value!;
                var existing = await ReceiptAsync(current.Instance.Id, request.IdempotencyKey, inner).ConfigureAwait(false);
                if (existing is not null) return Replay(current, actor, hash, existing);
                var valid = ValidateActive(current, request, clock.UtcNow);
                if (!valid.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(valid.Error!);
                if (current.Binding != initial.Binding || current.Asset.CanonicalJson != initial.Asset.CanonicalJson ||
                    !WorkflowVoteSubmissionRules.TryCompile(current.BallotSchema, request.Values, request.ChoiceKey,
                        current.Policy.AllowAbstain, out var checkedSubmission) || checkedSubmission!.EvidenceHash != submission.EvidenceHash)
                    return Conflict();
                var action = current.Actions.Validate("vote", request.Comment, request.SignatureFileId);
                if (!action.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(action.Error!);
                var now = clock.UtcNow;
                if (await command.ExecuteAsync(WorkflowSql.CompleteTodoWithRevision, P(("Id", todoId), ("AssigneeUserId", actor),
                    ("TenantScopeKey", scope.TenantScopeKey), ("CompletedAtUtc", now), ("ResultActionKey", "vote"),
                    ("Revision", request.ExpectedRevision)), inner).ConfigureAwait(false) != 1 ||
                    await command.ExecuteAsync(WorkflowVoteSql.SubmitBallot, P(("Id", current.Ballot!.Id), ("VoteId", current.Todo.StepId),
                    ("InstanceId", current.Instance.Id), ("TodoId", todoId), ("ActorUserId", actor), ("TenantScopeKey", scope.TenantScopeKey),
                    ("Revision", request.ExpectedBallotRevision), ("ChoiceKey", submission.ChoiceKey), ("SubmissionJson", submission.ValuesJson),
                    ("Now", now)), inner).ConfigureAwait(false) != 1) return Conflict();
                if (await command.ExecuteAsync(WorkflowVoteSql.SetViewOutput, P(("TodoId", todoId), ("InstanceId", current.Instance.Id),
                    ("ActorUserId", actor), ("BallotId", current.Ballot.Id), ("TenantScopeKey", scope.TenantScopeKey), ("OutputJson", submission.EvidenceJson),
                    ("OutputRevision", request.ExpectedBallotRevision + 1)), inner).ConfigureAwait(false) != 1) return Conflict();
                var writtenFiles = await attachments.WriteAsync(files.Value, current.Ballot, current.Instance.Id, actor, scope.TenantScopeKey,
                    current.BallotSchema, request.Values, submission.EvidenceHash, inner).ConfigureAwait(false);
                if (!writtenFiles.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(writtenFiles.Error!);
                var writtenSignature = await signatures.WriteForFormAsync(signature, current.Todo, current.Instance, actor, "vote",
                    request.IdempotencyKey, request.SignatureFileId, current.Binding.BallotFormVersionId, request.ExpectedBallotRevision + 1,
                    submission.EvidenceJson, inner).ConfigureAwait(false);
                if (!writtenSignature.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(writtenSignature.Error!);
                var decision = await decisions.ResolveAsync(current.Round, current.Policy, scope, now, false, inner).ConfigureAwait(false);
                if (!decision.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(decision.Error!);
                var applied = await results.ApplyAsync(current, initial, decision.Value!, preparedAssignees, scope, actor, now, inner).ConfigureAwait(false);
                if (!applied.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(applied.Error!);
                var response = applied.Value!;
                if (await command.ExecuteAsync(WorkflowSql.InsertApprovalActionRecord, P(("Id", ids.NewId()), ("InstanceId", current.Instance.Id),
                    ("StepId", current.Todo.StepId), ("TodoId", todoId), ("ActionKey", "vote"), ("ActorUserId", actor),
                    ("InstanceRevision", response.Revision), ("IdempotencyKey", request.IdempotencyKey.Trim()),
                    ("CommentSummary", request.Comment?.Trim()), ("CreatedAtUtc", now), ("ResultStatusKey", response.StatusKey),
                    ("ResultTodoId", response.ActiveTodoId)), inner).ConfigureAwait(false) != 1) return Conflict();
                var correlation = Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
                await command.ExecuteAsync(WorkflowSql.InsertExecutionLog, P(("Id", ids.NewId()), ("InstanceId", current.Instance.Id),
                    ("StepId", current.Todo.StepId), ("TransitionKey", "todo.vote"), ("FromStatusKey", "active"),
                    ("ToStatusKey", response.StatusKey), ("IdempotencyKey", request.IdempotencyKey.Trim()), ("Summary", hash),
                    ("CreatedAtUtc", now), ("CorrelationJson", correlation)), inner).ConfigureAwait(false);
                await command.ExecuteAsync(WorkflowSql.InsertDomainAudit, P(("Id", ids.NewId()), ("TenantId", scope.TenantId), ("ScopeKey", scope.ScopeKey),
                    ("InstanceId", current.Instance.Id), ("OperationKey", "todo.vote"), ("ActorUserId", actor), ("ResourceTypeKey", "todo"),
                    ("ResourceId", todoId), ("OutcomeKey", "succeeded"), ("DetailJson", "{\"submissionHash\":\"" + submission.EvidenceHash + "\"}"),
                    ("CreatedAtUtc", now), ("CorrelationJson", correlation)), inner).ConfigureAwait(false);
                return Result<WorkflowInstanceResponse>.Success(response);
            }, token).ConfigureAwait(false);
        }
        finally
        {
            await signatures.FinishAsync(signature, token).ConfigureAwait(false);
            await attachments.FinishAsync(files.Value, token).ConfigureAwait(false);
        }
    }

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

    private static Result<bool> ValidateActive(WorkflowVoteActionContext context, VoteWorkflowTodoRequest request, DateTimeOffset now) =>
        context.Instance.StatusKey != "active" || context.Todo.StatusKey != "active" || context.Todo.Revision != request.ExpectedRevision ||
        context.Round.StatusKey != "active" || context.Binding.FormStatusKey != "active" || context.Ballot is not { StatusKey: "pending" } ||
        context.Ballot.Revision != request.ExpectedBallotRevision || context.Ballot.TodoId != context.Todo.Id ||
        context.Round.ClosesAtUtc <= now ? Result<bool>.Failure(new(WorkflowErrorCodes.RevisionConflict, "The private ballot changed or closed.", ErrorType.Conflict)) : Result<bool>.Success(true);

    private Task<WorkflowActionReceiptRecord?> ReceiptAsync(Guid instance, string key, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt,
            P(("InstanceId", instance), ("IdempotencyKey", key.Trim())), token);
    private static Result<WorkflowInstanceResponse> Replay(WorkflowVoteActionContext context, Guid actor, string hash, WorkflowActionReceiptRecord receipt) =>
        receipt.TodoId != context.Todo.Id || receipt.ActorUserId != actor || receipt.ActionKey != "vote" || receipt.RequestHash != hash || receipt.ResultStatusKey is null
            ? Conflict() : Result<WorkflowInstanceResponse>.Success(new(context.Instance.Id, context.Instance.DefinitionVersionId,
                context.Instance.FormVersionId!.Value, context.Instance.BusinessType, context.Instance.BusinessId, context.Instance.BusinessTitle,
                receipt.ResultStatusKey, receipt.InstanceRevision, receipt.ResultTodoId, context.Instance.StartedAtUtc));
    private static object P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<WorkflowInstanceResponse> Fail(string code, ErrorType type) => Result<WorkflowInstanceResponse>.Failure(new(code, "The vote cannot be submitted.", type));
    private static Result<WorkflowInstanceResponse> Conflict() => Fail(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
    private static Result<WorkflowVoteActionContext> ContextFail(string code, ErrorType type) => Result<WorkflowVoteActionContext>.Failure(new(code, "The private ballot is unavailable.", type));
}

/// <summary>事务内重读的投票上下文，不作为HTTP响应且不共享个人答案。</summary>
internal sealed record WorkflowVoteActionContext(WorkflowTodoRuntimeRecord Todo, WorkflowInstanceRecord Instance,
    WorkflowRuntimeAssetRecord Asset, WorkflowVoteRoundRecord Round, WorkflowVoteFormBindingRecord Binding,
    WorkflowVoteBallotRecord? Ballot, WorkflowVotePolicy Policy, WorkflowFormSchema MainSchema, WorkflowFormSchema BallotSchema,
    WorkflowRuntimePlan RuntimePlan, WorkflowHumanActionPolicy Actions, WorkflowFormSubmissionRecord MainSubmission);
