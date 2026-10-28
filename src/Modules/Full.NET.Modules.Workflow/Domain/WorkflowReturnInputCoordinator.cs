using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>整体退回只把来源分支相对共同输入的差量投影到仍存活外层；不覆盖无关字段。</summary>
internal sealed class WorkflowReturnInputCoordinator(IQueryExecutor query, WorkflowBranchStateCoordinator branches)
{
    public async Task<WorkflowReturnInput?> PrepareAsync(WorkflowInstanceRecord instance, WorkflowTodoRuntimeRecord todo,
        WorkflowReturnScopeState state, WorkflowReturnScopeSelection selected, WorkflowFormSubmissionRecord main,
        WorkflowFormSubmissionRecord source, string patchedSource, CancellationToken token)
    {
        if (selected.ParallelJoinId == todo.ParallelJoinId && selected.ParallelBranchKey == todo.ParallelBranchKey)
            return new(patchedSource, source.Revision + 1, null, default);
        var outer = state.Forks.Where(fork => selected.ClosedForkIds.Contains(fork.Id) &&
            fork.ParentParallelJoinId == selected.ParallelJoinId && fork.ParentBranchKey == selected.ParallelBranchKey).ToArray();
        if (outer.Length != 1) return null;
        var frozen = await query.QueryAsync<WorkflowBranchStateRecord>(WorkflowBranchSql.List,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey), ("ParallelJoinId", outer[0].Id)), token).ConfigureAwait(false);
        if (frozen.Count is < 1 or > WorkflowDefinitionCompiler.MaximumNodes || frozen.Any(branch => branch.InputJson != frozen[0].InputJson || branch.InputFormRevision != frozen[0].InputFormRevision)) return null;
        var parent = await branches.ReadAsync(instance.Id, selected.ParallelJoinId, selected.ParallelBranchKey, main, instance.TenantScopeKey, token).ConfigureAwait(false);
        if (parent is null || !TryMerge(frozen[0].InputJson, patchedSource, parent.SubmissionJson, out var merged, out var delta)) return null;
        return new(merged!, parent.Revision + 1, parent, delta);
    }

    /// <summary>仅来源改变的字段进入差量；外层同字段变化不一致时明确冲突，JSON对象按结构比较。</summary>
    internal static bool TryMerge(string baselineJson, string sourceJson, string parentJson, out string? merged, out JsonElement delta)
    {
        merged = null; delta = default;
        var baseline = Read(baselineJson); var source = Read(sourceJson); var parent = Read(parentJson);
        if (baseline is null || source is null || parent is null || baseline.Keys.Any(key => !source.ContainsKey(key))) return false;
        var changes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            var hadOriginal = baseline.TryGetValue(pair.Key, out var original);
            if (hadOriginal && JsonElement.DeepEquals(original, pair.Value)) continue;
            var hasCurrent = parent.TryGetValue(pair.Key, out var current);
            if (hasCurrent != hadOriginal || hasCurrent && !JsonElement.DeepEquals(current, original))
                if (!hasCurrent || !JsonElement.DeepEquals(current, pair.Value)) return false;
            changes.Add(pair.Key, pair.Value); parent[pair.Key] = pair.Value;
        }
        merged = WorkflowActivityScheduler.SerializeInput(parent);
        delta = JsonSerializer.SerializeToElement(changes, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        return true;
    }
    private static Dictionary<string, JsonElement>? Read(string json) => JsonSerializer.Deserialize(json, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
}

/// <summary>轮次输入与来源签名各有独立修订，整体退回不能把来源分支修订冒充外层表单修订。</summary>
internal sealed record WorkflowReturnInput(string Json, long Revision, WorkflowFormSubmissionRecord? Parent, JsonElement Delta);
