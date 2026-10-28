using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>投票汇总后仅按声明结果字段回填本分支，个人答案始终留在独立票据。</summary>
internal sealed class WorkflowVoteResultWriter(IQueryExecutor query, ICommandExecutor command,
    WorkflowBranchStateCoordinator branches, WorkflowRuntimeTransitionExecutor transitions,
    WorkflowParallelJoinCoordinator joins, WorkflowNotificationOutboxPublisher notifications, WorkflowHumanActivityCompletionCoordinator callbacks)
{
    public static Dictionary<string, JsonElement>? ResultValues(WorkflowVoteActionContext context, bool result, bool previewCallback = true)
    {
        var values = JsonSerializer.Deserialize(context.MainSubmission.SubmissionJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (values is null) return null;
        if (context.Policy.ResultFieldKey is { } key)
        {
            var field = context.MainSchema.Sections.SelectMany(section => section.Fields).SingleOrDefault(field => field.FieldKey == key);
            if (field?.FieldTypeKey != "switch" || !WorkflowNodeFieldPolicy.TryResolve(context.Asset.CanonicalJson, context.Todo.NodeKey,
                context.MainSchema, out var policy) || !policy!.TryApplyPatch(context.MainSchema, values, ResultPatch(key, result), out var patched)) return null;
            values = patched!;
        }
        if (!previewCallback) return values;
        if (!context.RuntimePlan.TryGetResultCallbackPolicy(context.Todo.NodeKey, out var callback)) return null;
        return callback?.Apply(values, result) ?? values;
    }

    internal static JsonElement ResultPatch(string key, bool value)
    {
        using var document = JsonDocument.Parse(WorkflowJsonCanonicalizer.Compile(writer =>
        { writer.WriteStartObject(); writer.WriteBoolean(key, value); writer.WriteEndObject(); }).CanonicalJson);
        return document.RootElement.Clone();
    }

    public async Task<Result<WorkflowInstanceResponse>> ApplyAsync(WorkflowVoteActionContext current, WorkflowVoteActionContext prepared,
        WorkflowVoteDecision decision, WorkflowPreparedHumanActivities preparedAssignees, WorkflowManagementScope scope,
        Guid? actor, DateTimeOffset now, CancellationToken token)
    {
        var todo = current.Todo; var instance = current.Instance;
        if (decision.Outcome == WorkflowVoteOutcome.Invalid) return Conflict();
        var final = decision.Outcome != WorkflowVoteOutcome.Waiting;
        var values = JsonSerializer.Deserialize(current.MainSubmission.SubmissionJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (values is null) return Invalid();
        if (decision.Outcome is WorkflowVoteOutcome.Approved or WorkflowVoteOutcome.Rejected)
        {
            // 只有最终通过才消费事务外解析的下一人；其条件输入不能在准备后被另一分支改写。
            if (decision.Outcome == WorkflowVoteOutcome.Approved && (current.MainSubmission.Revision != prepared.MainSubmission.Revision ||
                current.MainSubmission.SubmissionJson != prepared.MainSubmission.SubmissionJson)) return Conflict();
            values = ResultValues(current, decision.Outcome == WorkflowVoteOutcome.Approved, previewCallback: false);
            if (values is null) return Invalid();
            if (current.Policy.ResultFieldKey is { } key)
            {
                var json = JsonSerializer.Serialize(values, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
                var written = todo.ParallelJoinId is not null
                    ? await branches.WriteAsync(todo, ResultPatch(key, decision.Outcome == WorkflowVoteOutcome.Approved),
                        current.MainSubmission.Revision, scope.TenantScopeKey, token).ConfigureAwait(false)
                    : await command.ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision,
                        P(("InstanceId", instance.Id), ("FormVersionId", instance.FormVersionId), ("SubmissionJson", json),
                            // 后台派生字段保留最后一名真实填表人；系统结果来源单独记入审计，不伪造候选投票。
                            ("UpdatedById", actor ?? current.MainSubmission.UpdatedById), ("UpdatedAtUtc", now), ("Revision", current.MainSubmission.Revision)), token).ConfigureAwait(false);
                if (written != 1) return Conflict();
            }
            var callback = await callbacks.ApplyAsync(instance, todo.StepId, todo.NodeKey, current.RuntimePlan, values,
                decision.Outcome == WorkflowVoteOutcome.Approved, actor, todo.ParallelJoinId, todo.ParallelBranchKey, now, token).ConfigureAwait(false);
            if (!callback.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(callback.Error!);
            values = callback.Value!.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
        var status = decision.Outcome switch { WorkflowVoteOutcome.Rejected => "rejected", WorkflowVoteOutcome.Suspended => "suspended", _ => "active" };
        var step = final ? WorkflowSql.CompleteStepWithRevision : WorkflowSql.AdvanceApprovalStepWithRevision;
        if (await command.ExecuteAsync(step, P(("Id", todo.StepId), ("InstanceId", instance.Id), ("Revision", todo.StepRevision),
            ("StatusKey", decision.Outcome == WorkflowVoteOutcome.Rejected ? "rejected" : decision.Outcome == WorkflowVoteOutcome.Suspended ? "suspended" : "completed"),
            ("CompletedAtUtc", now)), token).ConfigureAwait(false) != 1) return Conflict();
        Guid? nextTodo = null;
        if (decision.Outcome == WorkflowVoteOutcome.Approved)
        {
            if (!current.RuntimePlan.TryResolveAfterHuman(todo.NodeKey, values, todo.ParallelJoinNodeKey, out var transition)) return Invalid();
            var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,
                P(("InstanceId", instance.Id)), token).ConfigureAwait(false);
            var execution = await transitions.ExecuteAsync(instance, scope, await WorkflowExecutionEpochScope.ReadForStepAsync(query, instance.Id, scope.TenantScopeKey, todo.StepId, token).ConfigureAwait(false) ?? Guid.Empty, current.RuntimePlan, transition, values, sequence, now,
                instance.StartedById, todo.ParallelJoinId, todo.ParallelBranchKey, token, preparedAssignees: preparedAssignees, sourceStepId: todo.StepId).ConfigureAwait(false);
            if (!execution.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(execution.Error!);
            nextTodo = execution.Value!.FirstTodoId;
            status = execution.Value.InstanceStatusKey;
        }
        var instanceStatement = status switch { "active" => WorkflowSql.AdvanceInstanceWithRevision,
            "suspended" => WorkflowSql.SuspendInstanceWithRevision, _ => WorkflowSql.CompleteInstanceWithRevision };
        if (await command.ExecuteAsync(instanceStatement, P(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
            ("Revision", instance.Revision), ("StatusKey", status), ("CompletedAtUtc", now)), token).ConfigureAwait(false) != 1) return Conflict();
        if (status == "rejected")
        {
            await WorkflowHumanActivityLifecycle.CancelAsync(command, instance.Id, scope.TenantScopeKey, now, token).ConfigureAwait(false);
            await command.ExecuteAsync(WorkflowActivitySql.CancelApprovalSlots, P(("InstanceId", instance.Id), ("Now", now)), token).ConfigureAwait(false);
            await command.ExecuteAsync(WorkflowSql.CancelAllActiveTodosByInstance, P(("InstanceId", instance.Id),
                ("CompletedAtUtc", now), ("ResultActionKey", "cancelled")), token).ConfigureAwait(false);
            await command.ExecuteAsync(WorkflowSql.CancelAllActiveStepsByInstance, P(("InstanceId", instance.Id),
                ("CompletedAtUtc", now), ("StatusKey", "cancelled")), token).ConfigureAwait(false);
            await joins.CancelWaitingJoinsAsync(instance.Id, now, token).ConfigureAwait(false);
        }
        if (status is "completed" or "rejected")
            await command.ExecuteAsync(WorkflowActivitySql.CancelWork, P(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey), ("Now", now)), token).ConfigureAwait(false);
        if (status == "completed")
            await notifications.PublishInstanceCompletedAsync(instance.Id, instance.StartedById, instance.BusinessType,
                instance.BusinessId, now, token).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(new(instance.Id, instance.DefinitionVersionId, instance.FormVersionId!.Value,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, status, instance.Revision + 1, nextTodo, instance.StartedAtUtc));
    }

    private static object P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<WorkflowInstanceResponse> Conflict() => Result<WorkflowInstanceResponse>.Failure(new(WorkflowErrorCodes.RevisionConflict, "The vote transition changed.", ErrorType.Conflict));
    private static Result<WorkflowInstanceResponse> Invalid() => Result<WorkflowInstanceResponse>.Failure(new(WorkflowErrorCodes.SchemaInvalid, "The vote result binding is invalid.", ErrorType.BusinessRule));
}
