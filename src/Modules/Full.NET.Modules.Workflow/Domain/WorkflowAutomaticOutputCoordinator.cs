using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>本地字段与外部结果共享表单接纳边界：准备文件在事务外，接纳只写Workflow事实。</summary>
internal sealed class WorkflowAutomaticOutputCoordinator(IQueryExecutor query, ICommandExecutor command, IClock clock,
    WorkflowBranchStateCoordinator branches, WorkflowFormAttachmentCoordinator attachments)
{
    /// <summary>发送外部请求前只核对固定输入，不执行文件授权或持有跨网络事务。</summary>
    public async Task<bool> MatchesCurrentAsync(WorkflowInstanceRecord instance, WorkflowAutomaticInput input,
        IReadOnlyDictionary<string, JsonElement> values, CancellationToken token) =>
        Matches(await ReadCurrentAsync(instance, input, token).ConfigureAwait(false), input, values);
    public async Task<WorkflowPreparedAutomaticOutput?> PrepareAsync(WorkflowInstanceRecord instance, WorkflowAutomaticInput input,
        WorkflowFormSchema schema, IReadOnlyDictionary<string, JsonElement> values, WorkflowFieldMutationOutput output,
        Func<string, bool> canRead, CancellationToken token, IReadOnlyDictionary<Guid, WorkflowAttachmentReuseSource>? reuseSources = null)
    {
        var submission = await ReadCurrentAsync(instance, input, token).ConfigureAwait(false);
        if (!Matches(submission, input, values)) return null;
        var next = Serialize(output.Values);
        // 聚合可超过任一单项的大小；先拒绝，避免提交必失败却反复申请Files临时持有。
        if (Encoding.UTF8.GetByteCount(next) > 131072) return null;
        var visible = values.Where(pair => canRead(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var files = WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, visible).Values.SelectMany(ids => ids).ToHashSet();
        var prepared = await attachments.PrepareAsync(instance.StartedById, submission!.Id, instance.Id, instance.TenantScopeKey,
            schema, values, output.Values, files, submission.Revision, next, token, reuseSources).ConfigureAwait(false);
        return prepared.IsSuccess ? new(output, next, prepared.Value!) : null;
    }

    public async Task<bool> CommitAsync(WorkflowInstanceRecord instance, WorkflowAutomaticInput input, WorkflowFormSchema schema,
        IReadOnlyDictionary<string, JsonElement> values, WorkflowPreparedAutomaticOutput prepared, CancellationToken token)
    {
        var submission = await ReadCurrentAsync(instance, input, token).ConfigureAwait(false);
        if (!Matches(submission, input, values)) return false;
        var next = values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        foreach (var field in prepared.Output.Patch) next[field.Key] = field.Value.Clone();
        if (Serialize(next) != prepared.OutputJson || !WorkflowFormValueValidator.Validate(schema, next) ||
            Encoding.UTF8.GetByteCount(prepared.OutputJson) > 131072) return false;
        if (!prepared.Output.Matched) return prepared.Output.Patch.Count == 0;
        using var document = JsonDocument.Parse(Serialize(prepared.Output.Patch));
        var updated = input.ParallelJoinId is not null
            ? await branches.WriteAsync(instance.Id, input.ParallelJoinId, input.ParallelBranchKey, document.RootElement,
                input.FormRevision, instance.TenantScopeKey, token).ConfigureAwait(false)
            : await command.ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("FormVersionId", instance.FormVersionId),
                    ("SubmissionJson", prepared.OutputJson), ("UpdatedById", instance.StartedById), ("UpdatedAtUtc", clock.UtcNow),
                    ("Revision", input.FormRevision)), token).ConfigureAwait(false);
        return updated == 1 && (await attachments.WritePreparedAsync(prepared.AttachmentPlan, instance.StartedById, submission!, instance.Id,
            instance.TenantScopeKey, schema, values, prepared.Output.Values, prepared.OutputJson, token).ConfigureAwait(false)).IsSuccess;
    }

    public Task FinishAsync(WorkflowPreparedAutomaticOutput? prepared, CancellationToken token) => attachments.FinishAsync(prepared?.AttachmentPlan, token);

    /// <summary>异步在结果消费者处读取真实当前输入，保留独立字段变化；调用方还须核对原目标未变。</summary>
    public async Task<WorkflowAutomaticSnapshot?> ReadSnapshotAsync(WorkflowInstanceRecord instance, Guid? joinId, string? branchKey,
        WorkflowFormSchema schema, CancellationToken token)
    {
        var current = await ReadCurrentAsync(instance,joinId,branchKey,token).ConfigureAwait(false);
        if (current is null || Encoding.UTF8.GetByteCount(current.SubmissionJson)>131072) return null;
        try
        {
            var values=JsonSerializer.Deserialize(current.SubmissionJson,WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            if (values is null || !WorkflowFormValueValidator.Validate(schema,values)) return null;
            var json=Serialize(values); return new(json,Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))),current.Revision,values);
        }
        catch(JsonException) { return null; }
    }

    private Task<WorkflowFormSubmissionRecord?> ReadCurrentAsync(WorkflowInstanceRecord instance, WorkflowAutomaticInput input, CancellationToken token) =>
        ReadCurrentAsync(instance,input.ParallelJoinId,input.ParallelBranchKey,token);

    private async Task<WorkflowFormSubmissionRecord?> ReadCurrentAsync(WorkflowInstanceRecord instance, Guid? joinId, string? branchKey, CancellationToken token)
    {
        var root = await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        return await branches.ReadAsync(instance.Id,joinId,branchKey,root,instance.TenantScopeKey,token).ConfigureAwait(false);
    }

    private static bool Matches(WorkflowFormSubmissionRecord? submission, WorkflowAutomaticInput input, IReadOnlyDictionary<string, JsonElement> values) =>
        submission?.Revision == input.FormRevision && Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input.Json))) == input.Hash &&
        JsonSerializer.Deserialize(input.Json, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement) is { } original && Serialize(original) == Serialize(values) &&
        JsonSerializer.Deserialize(submission.SubmissionJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement) is { } current && Serialize(current) == Serialize(values);

    private static string Serialize(IReadOnlyDictionary<string, JsonElement> values) => JsonSerializer.Serialize(
        values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
}

/// <summary>真实自动激活的不可变输入，不携带人工身份或伪造Todo。</summary>
internal sealed record WorkflowAutomaticInput(Guid ActivationId, string Json, string Hash, long FormRevision, Guid? ParallelJoinId, string? ParallelBranchKey);
/// <summary>事务外文件准备只对同一固定输入及输出摘要有效。</summary>
internal sealed record WorkflowPreparedAutomaticOutput(WorkflowFieldMutationOutput Output, string OutputJson, WorkflowAttachmentPlan AttachmentPlan);
/// <summary>结果接纳前的实际当前快照；不替代外部调用自身的固定请求输入。</summary>
internal sealed record WorkflowAutomaticSnapshot(string Json,string Hash,long FormRevision,IReadOnlyDictionary<string,JsonElement> Values);
