using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>分叉冻结共同输入，分支只累计自身输出；汇合按稳定分支键合并不相交字段。</summary>
internal sealed class WorkflowBranchStateCoordinator(IQueryExecutor query, ICommandExecutor command)
{
    public async Task CreateAsync(Guid joinId, WorkflowInstanceRecord instance,
        IReadOnlyList<WorkflowParallelBranchPlan> branches, IReadOnlyDictionary<string, JsonElement> values,
        Guid? parentJoinId, string? parentBranchKey, CancellationToken token)
    {
        var submission = await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(
            WorkflowSql.FindFormSubmissionByInstance, WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        if (submission is null) throw new InvalidOperationException("workflow.branch.input_missing");
        var revision = submission.Revision;
        if (parentJoinId is { } parent)
        {
            var parentBranch = parentBranchKey is null ? null : await FindAsync(instance.Id, parent,
                parentBranchKey, instance.TenantScopeKey, token).ConfigureAwait(false);
            if (parentBranch is null) throw new InvalidOperationException("workflow.branch.parent_missing");
            revision = parentBranch.Revision;
        }
        await command.ExecuteAsync(WorkflowBranchSql.InsertContext,
            WorkflowSqlParameters.Create(("ParallelJoinId", joinId), ("ParentParallelJoinId", parentJoinId),
                ("ParentBranchKey", parentBranchKey)), token).ConfigureAwait(false);
        var input = Serialize(values);
        foreach (var branch in branches)
            await command.ExecuteAsync(WorkflowBranchSql.Insert,
                WorkflowSqlParameters.Create(("ParallelJoinId", joinId), ("BranchKey", branch.BranchKey),
                    ("InputJson", input), ("InputFormRevision", revision)), token).ConfigureAwait(false);
    }

    public async Task<WorkflowFormSubmissionRecord?> ReadAsync(WorkflowTodoRuntimeRecord todo,
        WorkflowFormSubmissionRecord? submission, string tenantScopeKey, CancellationToken token)
        => await ReadAsync(todo.InstanceId, todo.ParallelJoinId, todo.ParallelBranchKey, submission, tenantScopeKey, token).ConfigureAwait(false);

    /// <summary>领取池尚无Todo时仍按真实步骤分支读取表单，禁止退回主表单或构造假待办。</summary>
    public async Task<WorkflowFormSubmissionRecord?> ReadAsync(Guid instanceId, Guid? parallelJoinId, string? parallelBranchKey,
        WorkflowFormSubmissionRecord? submission, string tenantScopeKey, CancellationToken token)
    {
        if (parallelJoinId is not { } joinId) return submission;
        if (submission is null || parallelBranchKey is not { } branchKey) return null;
        var branch = await FindAsync(instanceId, joinId, branchKey, tenantScopeKey, token).ConfigureAwait(false);
        if (branch is null) return null;
        var values = Deserialize(branch.InputJson);
        foreach (var pair in Deserialize(branch.OutputJson)) values[pair.Key] = pair.Value;
        return submission with { SubmissionJson = Serialize(values), Revision = branch.Revision };
    }

    public async Task<int> WriteAsync(WorkflowTodoRuntimeRecord todo, JsonElement patch, long expectedRevision,
        string tenantScopeKey, CancellationToken token)
        => await WriteAsync(todo.InstanceId, todo.ParallelJoinId, todo.ParallelBranchKey, patch, expectedRevision,
            tenantScopeKey, token).ConfigureAwait(false);

    /// <summary>自动活动按真实分支身份写入，不能构造人工Todo来复用字段提交。</summary>
    public async Task<int> WriteAsync(Guid instanceId, Guid? parallelJoinId, string? parallelBranchKey,
        JsonElement patch, long expectedRevision, string tenantScopeKey, CancellationToken token)
    {
        if (parallelJoinId is not { } joinId || parallelBranchKey is not { } branchKey) return 0;
        var branch = await FindAsync(instanceId, joinId, branchKey, tenantScopeKey, token).ConfigureAwait(false);
        if (branch?.Revision != expectedRevision) return 0;
        var output = Deserialize(branch.OutputJson);
        foreach (var field in patch.EnumerateObject()) output[field.Name] = field.Value.Clone();
        return await command.ExecuteAsync(WorkflowBranchSql.Update,
            WorkflowSqlParameters.Create(("ParallelJoinId", joinId), ("BranchKey", branchKey),
                ("Revision", expectedRevision), ("OutputJson", Serialize(output))), token).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyDictionary<string, JsonElement>>> MergeAsync(
        WorkflowInstanceRecord instance, Guid joinId, Guid actorId, DateTimeOffset now, CancellationToken token)
    {
        var branches = await query.QueryAsync<WorkflowBranchStateRecord>(WorkflowBranchSql.List,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("ParallelJoinId", joinId),
                ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        if (branches.Count == 0) return Conflict();
        var first = branches[0];
        var merged = Deserialize(first.InputJson);
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var branch in branches.OrderBy(branch => branch.BranchKey, StringComparer.Ordinal))
        {
            if (branch.InputFormRevision != first.InputFormRevision || branch.InputJson != first.InputJson) return Conflict();
            foreach (var field in Deserialize(branch.OutputJson))
            {
                // 没有已发布的归并器时，即使值相同也不能隐式决定两个写入者的优先级。
                if (!written.Add(field.Key)) return Conflict();
                merged[field.Key] = field.Value;
            }
        }
        var json = Serialize(merged);
        var context = await ReadContextAsync(instance, joinId, token).ConfigureAwait(false);
        if (context is null) return Conflict();
        int updated;
        if (context?.ParentParallelJoinId is { } parent && context.ParentBranchKey is { } parentKey)
        {
            var parentBranch = await FindAsync(instance.Id, parent, parentKey, instance.TenantScopeKey, token).ConfigureAwait(false);
            if (parentBranch?.Revision != first.InputFormRevision) return Conflict();
            var output = Deserialize(parentBranch.OutputJson);
            foreach (var field in written) output[field] = merged[field];
            updated = await command.ExecuteAsync(WorkflowBranchSql.Update,
                WorkflowSqlParameters.Create(("ParallelJoinId", parent), ("BranchKey", parentKey),
                    ("Revision", first.InputFormRevision), ("OutputJson", Serialize(output))), token).ConfigureAwait(false);
        }
        else
        {
            updated = await command.ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("FormVersionId", instance.FormVersionId),
                    ("SubmissionJson", json), ("UpdatedById", actorId), ("UpdatedAtUtc", now),
                    ("Revision", first.InputFormRevision)), token).ConfigureAwait(false);
        }
        if (updated != 1) return Conflict();
        await command.ExecuteAsync(WorkflowBranchSql.InsertMerge,
            WorkflowSqlParameters.Create(("ParallelJoinId", joinId), ("FormRevision", first.InputFormRevision + 1),
                ("OutputJson", json), ("MergedAtUtc", now)), token).ConfigureAwait(false);
        return Result<IReadOnlyDictionary<string, JsonElement>>.Success(merged);
    }

    public Task<WorkflowForkContextRecord?> ReadContextAsync(WorkflowInstanceRecord instance, Guid joinId, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowForkContextRecord>(WorkflowBranchSql.FindContext,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("ParallelJoinId", joinId),
                ("TenantScopeKey", instance.TenantScopeKey)), token);

    public async Task<long?> ReadRevisionAsync(WorkflowInstanceRecord instance, Guid? joinId, string? branchKey,
        CancellationToken token)
    {
        if (joinId is { } join)
            return branchKey is null ? null : (await FindAsync(instance.Id, join, branchKey,
                instance.TenantScopeKey, token).ConfigureAwait(false))?.Revision;
        return (await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey)), token)
            .ConfigureAwait(false))?.Revision;
    }

    private Task<WorkflowBranchStateRecord?> FindAsync(Guid instanceId, Guid joinId, string branch,
        string tenantScopeKey, CancellationToken token) => query.QuerySingleOrDefaultAsync<WorkflowBranchStateRecord>(
            WorkflowBranchSql.Find, WorkflowSqlParameters.Create(("InstanceId", instanceId), ("ParallelJoinId", joinId),
                ("BranchKey", branch), ("TenantScopeKey", tenantScopeKey)), token);

    private static Dictionary<string, JsonElement> Deserialize(string json) =>
        JsonSerializer.Deserialize(json, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement)!;

    private static string Serialize(IReadOnlyDictionary<string, JsonElement> values) => JsonSerializer.Serialize(
        values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.Ordinal), WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);

    private static Result<IReadOnlyDictionary<string, JsonElement>> Conflict() =>
        Result<IReadOnlyDictionary<string, JsonElement>>.Failure(new Error(WorkflowErrorCodes.RevisionConflict,
            "The workflow branch outputs conflict with the frozen input or another branch.", ErrorType.Conflict));
}
