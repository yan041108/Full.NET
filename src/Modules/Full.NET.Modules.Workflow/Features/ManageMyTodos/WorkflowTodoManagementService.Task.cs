using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>独立办理提交路径复用受信表单和证据边界，但不写审批席位或同意票。</summary>
internal sealed partial class WorkflowTodoManagementService
{
    public Task<Result<WorkflowInstanceResponse>> CompleteAsync(Guid todoId, Guid actorUserId,
        ActWorkflowTodoRequest request, CancellationToken cancellationToken = default) =>
        ExecutePreparedAsync(todoId, actorUserId, request, "complete",
            (prepared, token) => ActAsync(todoId, actorUserId, request, "complete", prepared, token), cancellationToken);

    private async Task<Result<WorkflowInstanceResponse>> ActTaskAsync(WorkflowTodoRuntimeRecord todo, WorkflowInstanceRecord instance,
        Guid formVersionId, WorkflowFormSubmissionRecord submission, WorkflowRuntimePlan runtimePlan, string patchedSubmission,
        WorkflowFormSchema schema, WorkflowManagementScope scope, Guid actor, ActWorkflowTodoRequest request, string requestHash,
        WorkflowActionPreparation preparation, CancellationToken token)
    {
        var task = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTaskContextRecord>(WorkflowTaskSql.FindForTodo,
            WorkflowSqlParameters.Create(("TodoId", todo.Id), ("TaskId", todo.StepId), ("InstanceId", instance.Id),
                ("ActorUserId", actor), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (task is null) return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        var values = JsonSerializer.Deserialize(patchedSubmission, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (values is null) return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        var now = clock.UtcNow;
        var formUpdated = todo.ParallelJoinId is not null
            ? await branchStates.WriteAsync(todo, request.FieldPatch, request.ExpectedFormRevision, scope.TenantScopeKey, token).ConfigureAwait(false)
            : await commandExecutor.ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("FormVersionId", formVersionId), ("SubmissionJson", patchedSubmission),
                    ("UpdatedById", actor), ("UpdatedAtUtc", now), ("Revision", request.ExpectedFormRevision)), token).ConfigureAwait(false);
        if (formUpdated != 1)
        {
            var concurrent = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("IdempotencyKey", request.IdempotencyKey.Trim())), token).ConfigureAwait(false);
            return ReplayApproval(todo.Id, instance, formVersionId, actor, "complete", requestHash, concurrent);
        }
        if (await commandExecutor.ExecuteAsync(WorkflowSql.CompleteTodoWithRevision,
                WorkflowSqlParameters.Create(("Id", todo.Id), ("AssigneeUserId", actor), ("TenantScopeKey", scope.TenantScopeKey),
                    ("CompletedAtUtc", now), ("ResultActionKey", "complete"), ("Revision", request.ExpectedRevision)), token).ConfigureAwait(false) != 1 ||
            await commandExecutor.ExecuteAsync(WorkflowTaskSql.CompleteMember,
                WorkflowSqlParameters.Create(("Id", task.Id), ("TaskId", task.TaskId), ("InstanceId", instance.Id), ("TodoId", todo.Id),
                    ("ActorUserId", actor), ("TenantScopeKey", scope.TenantScopeKey), ("Revision", task.Revision), ("Now", now)), token).ConfigureAwait(false) != 1)
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        var files = await SyncSubmissionAttachmentsAsync(actor, submission, instance.Id, scope, schema, submission.SubmissionJson,
            patchedSubmission, preparation.Attachments, token).ConfigureAwait(false);
        if (!files.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(files.Error!);
        var signature = await signatureCoordinator.WriteAsync(preparation.Signature, todo, instance, actor, "complete",
            request.IdempotencyKey, request.SignatureFileId, request.ExpectedFormRevision + 1, patchedSubmission, token).ConfigureAwait(false);
        if (!signature.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(signature.Error!);
        if (await RecordTodoOutputAsync(todo, scope, patchedSubmission, request.ExpectedFormRevision + 1, token).ConfigureAwait(false) != 1)
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        runtimePlan.TryGetHumanTimeoutPolicy(todo.NodeKey, out var timeout);
        var decision = await taskDecisions.ResolveAsync(task, todo, instance, scope, values, timeout, now, token).ConfigureAwait(false);
        if (!decision.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(decision.Error!);
        var result = decision.Value!;
        var stepUpdated = result.CompletesActivity
            ? await commandExecutor.ExecuteAsync(WorkflowSql.CompleteStepWithRevision,
                WorkflowSqlParameters.Create(("Id", todo.StepId), ("InstanceId", instance.Id), ("StatusKey", "completed"),
                    ("CompletedAtUtc", now), ("Revision", todo.StepRevision)), token).ConfigureAwait(false)
            : await commandExecutor.ExecuteAsync(WorkflowSql.AdvanceApprovalStepWithRevision,
                WorkflowSqlParameters.Create(("Id", todo.StepId), ("InstanceId", instance.Id), ("Revision", todo.StepRevision)), token).ConfigureAwait(false);
        if (stepUpdated != 1) return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        Guid? nextTodo = result.NextTodoId;
        var status = "active";
        if (result.CompletesActivity)
        {
            var callback = await resultCallbacks.ApplyAsync(instance, todo.StepId, todo.NodeKey, runtimePlan, values, true, actor,
                todo.ParallelJoinId, todo.ParallelBranchKey, now, token).ConfigureAwait(false);
            if (!callback.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(callback.Error!);
            values = callback.Value!.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (!runtimePlan.TryResolveAfterHuman(todo.NodeKey, values, todo.ParallelJoinNodeKey, out var transition))
                return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
            var sequence = await queryExecutor.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id)), token).ConfigureAwait(false);
            var execution = await transitionExecutor.ExecuteAsync(instance, scope, runtimePlan, transition, values, sequence, now,
                instance.StartedById, todo.ParallelJoinId, todo.ParallelBranchKey, token, preparedAssignees: preparation.Approvals).ConfigureAwait(false);
            if (!execution.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(execution.Error!);
            nextTodo = execution.Value!.FirstTodoId;
            status = execution.Value.InstanceStatusKey;
        }
        if (await commandExecutor.ExecuteAsync(status switch
            {
                "active" => WorkflowSql.AdvanceInstanceWithRevision,
                "suspended" => WorkflowSql.SuspendInstanceWithRevision,
                _ => WorkflowSql.CompleteInstanceWithRevision
            }, WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey), ("Revision", instance.Revision),
                ("StatusKey", status), ("CompletedAtUtc", now)), token).ConfigureAwait(false) != 1)
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        if (status == "rejected")
            await notificationPublisher.PublishInstanceRejectedAsync(instance.Id, instance.StartedById, instance.BusinessType, instance.BusinessId, now, token).ConfigureAwait(false);
        else if (status == "completed")
            await commandExecutor.ExecuteAsync(WorkflowActivitySql.CancelWork,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey), ("Now", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertApprovalActionRecord,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instance.Id), ("StepId", todo.StepId), ("TodoId", todo.Id),
                ("ActionKey", "complete"), ("ActorUserId", actor), ("InstanceRevision", instance.Revision + 1),
                ("IdempotencyKey", request.IdempotencyKey.Trim()), ("CommentSummary", request.Comment?.Trim()), ("CreatedAtUtc", now),
                ("ResultStatusKey", status), ("ResultTodoId", nextTodo)), token).ConfigureAwait(false);
        var correlation = Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(("CorrelationJson", correlation), ("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("TransitionKey", "todo.complete"), ("FromStatusKey", "active"), ("ToStatusKey", status),
                ("IdempotencyKey", request.IdempotencyKey.Trim()), ("Summary", requestHash), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            WorkflowSqlParameters.Create(("CorrelationJson", correlation), ("Id", idGenerator.NewId()), ("TenantId", scope.TenantId),
                ("ScopeKey", scope.ScopeKey), ("InstanceId", instance.Id), ("OperationKey", "todo.complete"), ("ActorUserId", actor),
                ("ResourceTypeKey", "todo"), ("ResourceId", todo.Id), ("OutcomeKey", "succeeded"),
                ("DetailJson", JsonSerializer.Serialize(new WorkflowTaskCompletionAuditDetail(task.CompletionModeKey,
                    task.RequiredCompletionCount, result.CompletedCount, result.PendingCount), WorkflowJsonSerializerContext.Default.WorkflowTaskCompletionAuditDetail)),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        if (status == "completed")
            await notificationPublisher.PublishInstanceCompletedAsync(instance.Id, instance.StartedById, instance.BusinessType, instance.BusinessId, now, token).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(new(instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, status, instance.Revision + 1, nextTodo, instance.StartedAtUtc));
    }
}

/// <summary>办理审计只记录完成数量，不伪造批准或驳回票数。</summary>
internal sealed record WorkflowTaskCompletionAuditDetail(string CompletionModeKey, int RequiredCompletionCount, int CompletedCount, int PendingCount);
