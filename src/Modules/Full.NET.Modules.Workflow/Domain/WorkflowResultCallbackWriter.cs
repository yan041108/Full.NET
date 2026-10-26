using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>调用方持有真实实例锁；布尔派生输出不会增删文件引用或改写个人签名证据。</summary>
internal sealed class WorkflowResultCallbackWriter(IQueryExecutor query, ICommandExecutor commands,
    WorkflowBranchStateCoordinator branches, IIdGenerator ids)
{
    public async Task<Result<WorkflowResultCallbackOutput>> ApplyAsync(WorkflowInstanceRecord instance, Guid stepId, string nodeKey,
        WorkflowRuntimePlan plan, IReadOnlyDictionary<string, JsonElement> expectedValues, bool accepted, Guid? attributionUserId,
        Guid? joinId, string? branchKey, DateTimeOffset now, CancellationToken token)
    {
        if (!plan.TryGetResultCallbackPolicy(nodeKey, out var policy)) return Invalid();
        if (policy is null) return Result<WorkflowResultCallbackOutput>.Success(new(expectedValues, null));
        var root = await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        var current = await branches.ReadAsync(instance.Id, joinId, branchKey, root, instance.TenantScopeKey, token).ConfigureAwait(false);
        if (current is null || current.InstanceId != instance.Id || current.FormVersionId != instance.FormVersionId ||
            Encoding.UTF8.GetByteCount(current.SubmissionJson) > 131072) return Invalid();
        Dictionary<string, JsonElement>? values;
        try { values = JsonSerializer.Deserialize(current.SubmissionJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement); }
        catch (JsonException) { return Invalid(); }
        if (values is null || Serialize(values) != Serialize(expectedValues)) return Conflict();
        var updated = policy.Apply(values, accepted); var json = Serialize(updated);
        if (Encoding.UTF8.GetByteCount(json) > 131072) return Invalid();
        var changed = json != Serialize(values); var revision = current.Revision;
        var fieldKey = JsonEncodedText.Encode(policy.FieldKey).ToString();
        if (changed)
        {
            using var patch = JsonDocument.Parse($"{{\"{fieldKey}\":{(accepted ? "true" : "false")}}}");
            var affected = joinId is not null
                ? await branches.WriteAsync(instance.Id, joinId, branchKey, patch.RootElement, revision, instance.TenantScopeKey, token).ConfigureAwait(false)
                : await commands.ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision,
                    WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("FormVersionId", instance.FormVersionId), ("SubmissionJson", json),
                        ("UpdatedById", attributionUserId ?? current.UpdatedById), ("UpdatedAtUtc", now), ("Revision", revision)), token).ConfigureAwait(false);
            if (affected != 1) return Conflict(); revision++;
        }
        var detail = $"{{\"sourceKey\":\"result_callback\",\"fieldKey\":\"{fieldKey}\",\"accepted\":{(accepted ? "true" : "false")},\"changed\":{(changed ? "true" : "false")}}}";
        var correlation = Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
        await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", instance.TenantId),
            ("ScopeKey", instance.ScopeKey), ("InstanceId", instance.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null),
            ("ResourceTypeKey", "step"), ("ResourceId", stepId), ("OutcomeKey", "succeeded"), ("DetailJson", detail),
            ("CorrelationJson", correlation), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        return Result<WorkflowResultCallbackOutput>.Success(new(updated, revision));
    }
    private static string Serialize(IReadOnlyDictionary<string, JsonElement> values) => JsonSerializer.Serialize(
        values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
    private static Result<WorkflowResultCallbackOutput> Invalid() => Result<WorkflowResultCallbackOutput>.Failure(new(WorkflowErrorCodes.SchemaInvalid,
        "The published result callback is invalid.", ErrorType.BusinessRule));
    private static Result<WorkflowResultCallbackOutput> Conflict() => Result<WorkflowResultCallbackOutput>.Failure(new(WorkflowErrorCodes.RevisionConflict,
        "The callback input changed.", ErrorType.Conflict));
}

/// <summary>仅布尔结果变化时递增输入修订；没有回填声明时保留原输入和调用方修订。</summary>
internal sealed record WorkflowResultCallbackOutput(IReadOnlyDictionary<string, JsonElement> Values, long? FormRevision);
