using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>字段执行复用原续办租约；文件授权在事务外准备，所有本模块事实在同一事务接纳。</summary>
internal sealed partial class WorkflowActivityWorkProcessor
{
    private async Task<WorkflowPreparedAutomaticOutput?> PrepareFieldAsync(WorkflowInstanceRecord instance,
        WorkflowActivityWorkRecord work, WorkflowFormSchema schema, WorkflowFieldWaitPlan wait,
        IReadOnlyDictionary<string, JsonElement> values, CancellationToken token)
    {
        if (!wait.Policy.TryApply(values, out var output)) return null;
        return await outputs.PrepareAsync(instance, FieldInput(work), schema, values, output!, wait.Policy.CanRead, token).ConfigureAwait(false);
    }

    private async Task<bool> ApplyFieldAsync(WorkflowInstanceRecord instance, WorkflowActivityWorkRecord work,
        WorkflowPreparedAutomaticOutput field, string nodeType, WorkflowFormSchema schema, IReadOnlyDictionary<string, JsonElement> values,
        long sequence, CancellationToken token, WorkflowActivationTrace trace)
    {
        if (await query.QuerySingleOrDefaultAsync<long>(WorkflowActivitySql.CountActivations,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id)), token).ConfigureAwait(false) >= WorkflowActivityScheduler.InstanceActivityBudget)
            return false;
        var now = clock.UtcNow;
        var patch = SerializeFieldValues(field.Output.Patch);
        if (!await outputs.CommitAsync(instance, FieldInput(work), schema, values, field, token).ConfigureAwait(false)) return false;
        var parameters = WorkflowSqlParameters.Create(("Id", work.Id), ("InstanceId", instance.Id), ("ExecutionEpochId", work.ExecutionEpochId),
            ("TenantScopeKey", instance.TenantScopeKey), ("NodeKey", work.NodeKey), ("NodeTypeKey", nodeType),
            ("ExecutionSequence", sequence), ("ParallelJoinId", work.ParallelJoinId), ("ParallelBranchKey", work.ParallelBranchKey),
            ("InputFormRevision", work.InputFormRevision), ("OutputFormRevision", work.InputFormRevision + (field.Output.Matched ? 1 : 0)),
            ("InputJson", work.InputJson), ("InputHash", work.InputHash), ("OutputJson", field.OutputJson),
            ("OutputHash", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(field.OutputJson)))) ,
            ("PatchJson", patch), ("OutcomeKey", field.Output.Matched ? "applied" : "skipped"), ("Now", now));
        await command.ExecuteAsync(WorkflowFieldActivitySql.InsertStep, parameters, token).ConfigureAwait(false);
        await WorkflowActivationBindingWriter.BindAsync(command,instance.Id,instance.TenantScopeKey,work.ExecutionEpochId,work.Id,now,token,trace,ids).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowFieldActivitySql.InsertResult, parameters, token).ConfigureAwait(false);
        var auditDetail = WorkflowJsonCanonicalizer.Compile(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("nodeTypeKey", nodeType);
            writer.WriteString("outcomeKey", field.Output.Matched ? "applied" : "skipped");
            writer.WritePropertyName("fieldKeys"); writer.WriteStartArray();
            foreach (var key in field.Output.Patch.Keys.Order(StringComparer.Ordinal)) writer.WriteStringValue(key);
            writer.WriteEndArray(); writer.WriteEndObject();
        }).CanonicalJson;
        await command.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", instance.TenantId), ("ScopeKey", instance.ScopeKey),
                ("InstanceId", instance.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null),
                ("ResourceTypeKey", "step"), ("ResourceId", work.Id), ("OutcomeKey", "succeeded"),
                ("DetailJson", auditDetail), ("CreatedAtUtc", now),
                ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson())), token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", work.Id),
                ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                ("TransitionKey", $"node.{nodeType}.{(field.Output.Matched ? "applied" : "skipped")}"),
                ("FromStatusKey", null), ("ToStatusKey", "completed"), ("IdempotencyKey", null), ("Summary", null),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        return true;
    }

    private static WorkflowAutomaticInput FieldInput(WorkflowActivityWorkRecord work) =>
        new(work.Id, work.InputJson, work.InputHash, work.InputFormRevision, work.ParallelJoinId, work.ParallelBranchKey);

    private static string SerializeFieldValues(IReadOnlyDictionary<string, JsonElement> values) => JsonSerializer.Serialize(
        values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);

    private sealed record PreparedActivityWork(WorkflowPreparedHumanActivities Humans, WorkflowPreparedAutomaticOutput? Field, bool InvalidField, bool InvalidAsyncResume = false, bool InvalidSource = false,
        bool InvalidReturnAssignees = false)
    {
        public static PreparedActivityWork Empty { get; } = new(WorkflowPreparedHumanActivities.Empty, null, false);
    }
}
