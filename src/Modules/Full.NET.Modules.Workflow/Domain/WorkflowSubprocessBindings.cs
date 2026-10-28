using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>发布时一次批量解析子版本并派生摘要、预算及深度；不信任草稿提供的子资产快照。</summary>
internal static class WorkflowSubprocessBindings
{
    public static async Task<Result<WorkflowDefinitionDraft>> BindAsync(WorkflowDefinitionDraft draft, WorkflowFormSchema parentSchema,
        WorkflowManagementScope scope, IQueryExecutor query, CancellationToken token, Guid parentDefinitionId)
    {
        if (draft.Nodes is null || draft.Nodes.Count > WorkflowDefinitionCompiler.MaximumNodes || draft.Nodes.Any(node => node is null) ||
            draft.Nodes.Select(node => node.NodeKey).Distinct(StringComparer.Ordinal).Count() != draft.Nodes.Count) return Invalid();
        var nodes = draft.Nodes.Where(node => node.NodeTypeKey == WorkflowSubprocessPolicy.NodeType).ToArray();
        if (nodes.Length == 0) return Result<WorkflowDefinitionDraft>.Success(draft);
        var policies = new Dictionary<string, WorkflowSubprocessPolicy>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!WorkflowSubprocessPolicy.TryRead(node.Config, parentSchema, out var policy)) return Invalid();
            policies.Add(node.NodeKey, policy!);
        }
        var assets = await query.QueryAsync<WorkflowRuntimeAssetRecord>(WorkflowSubprocessSql.FindAssets,
            WorkflowSqlParameters.Create(("DefinitionVersionIds", policies.Values.Select(item => item.DefinitionVersionId).Distinct().ToArray()),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (assets.Count > nodes.Length || assets.GroupBy(item => item.DefinitionVersionId).Any(group => group.Count() != 1)) return Invalid();
        var byId = assets.ToDictionary(item => item.DefinitionVersionId); var updated = new Dictionary<string, WorkflowNodeDraft>(StringComparer.Ordinal);
        if (!await ReferencesAllowedAsync(parentDefinitionId, byId, scope, query, token).ConfigureAwait(false)) return Invalid();
        foreach (var node in nodes)
        {
            var policy = policies[node.NodeKey];
            if (!byId.TryGetValue(policy.DefinitionVersionId, out var asset) ||
                !TryBindAsset(asset, policy, parentSchema, out var bound)) return Invalid();
            var config = node.Config.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal);
            config["boundSubprocess"] = JsonSerializer.SerializeToElement(bound, WorkflowJsonSerializerContext.Default.WorkflowBoundSubprocess);
            updated.Add(node.NodeKey, node with { Config = JsonSerializer.SerializeToElement(config, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement) });
        }
        var result = draft with { Nodes = draft.Nodes.Select(node => updated.GetValueOrDefault(node.NodeKey, node)).ToArray() };
        return TryReadBudget(result, out _, out _) ? Result<WorkflowDefinitionDraft>.Success(result) : Invalid();
    }

    private static async Task<bool> ReferencesAllowedAsync(Guid parentId, Dictionary<Guid, WorkflowRuntimeAssetRecord> assets,
        WorkflowManagementScope scope, IQueryExecutor query, CancellationToken token)
    {
        var versions = await query.QueryAsync<WorkflowDefinitionVersionRecord>(WorkflowSql.ListDefinitionVersions,
            WorkflowSqlParameters.Create(("DefinitionId", parentId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var forbidden = versions.Select(item => item.Id).ToHashSet(); var roots = assets.Keys.ToArray();
        var graph = new Dictionary<Guid, IReadOnlyList<Guid>>();
        while (graph.Count < assets.Count)
        {
            foreach (var asset in assets.Values.Where(item => !graph.ContainsKey(item.DefinitionVersionId)).ToArray())
            {
                if (forbidden.Contains(asset.DefinitionVersionId)) return false;
                WorkflowDefinitionDraft? child;
                try { child = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft); }
                catch (JsonException) { return false; }
                if (child?.Nodes is null || child.Nodes.Count > WorkflowDefinitionCompiler.MaximumNodes) return false;
                var references = new List<Guid>();
                foreach (var node in child.Nodes.Where(node => node.NodeTypeKey == WorkflowSubprocessPolicy.NodeType))
                {
                    if (!WorkflowSubprocessPolicy.TryRead(node.Config, null, out var policy)) return false;
                    references.Add(policy.DefinitionVersionId);
                }
                graph.Add(asset.DefinitionVersionId, references);
            }
            var missing = graph.Values.SelectMany(ids => ids).Distinct().Where(id => !assets.ContainsKey(id)).ToArray();
            if (missing.Any(forbidden.Contains) || assets.Count + missing.Length > WorkflowDefinitionCompiler.MaximumNodes) return false;
            if (missing.Length == 0) break;
            var found = await query.QueryAsync<WorkflowRuntimeAssetRecord>(WorkflowSubprocessSql.FindAssets,
                WorkflowSqlParameters.Create(("DefinitionVersionIds", missing), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (found.Count != missing.Length || found.Any(item => !missing.Contains(item.DefinitionVersionId)) || found.Select(item => item.DefinitionVersionId).Distinct().Count() != found.Count) return false;
            foreach (var item in found) assets.Add(item.DefinitionVersionId, item);
        }
        return ValidateReferenceGraph(graph, forbidden, roots);
    }

    /// <summary>固定版本也不能引用当前定义历史版本；共享子图可复用，但循环及过深调用失败关闭。</summary>
    internal static bool ValidateReferenceGraph(IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> graph, IReadOnlySet<Guid> forbidden, IReadOnlyList<Guid> roots)
    {
        if (graph.Count > WorkflowDefinitionCompiler.MaximumNodes || graph.Values.Sum(items => items.Count) > WorkflowDefinitionCompiler.MaximumEdges) return false;
        var heights = new Dictionary<Guid, int>(); var path = new HashSet<Guid>();
        int Height(Guid id, int depth)
        {
            if (depth > WorkflowSubprocessPolicy.MaximumDepth || forbidden.Contains(id) || !graph.TryGetValue(id, out var next)) return -1;
            if (heights.TryGetValue(id, out var known)) return depth + known - 1 <= WorkflowSubprocessPolicy.MaximumDepth ? known : -1;
            if (!path.Add(id)) return -1;
            var height = 1;
            foreach (var child in next) { var childHeight = Height(child, depth + 1); if (childHeight < 0) return -1; height = Math.Max(height, childHeight + 1); }
            path.Remove(id); heights.Add(id, height); return height;
        }
        return roots.All(id => Height(id, 1) > 0);
    }

    public static bool TryBindAsset(WorkflowRuntimeAssetRecord asset, WorkflowSubprocessPolicy policy, WorkflowFormSchema parent,
        [NotNullWhen(true)] out WorkflowBoundSubprocess? bound, bool requireEnabled = true)
    {
        bound = null;
        if (asset.DefinitionVersionId != policy.DefinitionVersionId || requireEnabled && !WorkflowDefinitionLifecycleRules.AllowsNewInstance(asset.DefinitionStatusKey) ||
            Encoding.UTF8.GetByteCount(asset.CanonicalJson) > 1048576 || Encoding.UTF8.GetByteCount(asset.FormSchemaJson) > 1048576) return false;
        WorkflowDefinitionDraft? child; WorkflowFormSchema? schema;
        try { child = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
            schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema); }
        catch (JsonException) { return false; }
        if (child is null || schema is null || child.Nodes.Any(node => node.NodeTypeKey == "business.command") ||
            !WorkflowRuntimePlan.TryCreate(child, schema, out _) || !policy.MatchesSchemas(parent, schema) || !TryReadBudget(child, out var budget, out var depth)) return false;
        bound = new(asset.DefinitionVersionId, asset.FormVersionId, Hash(asset.CanonicalJson), Hash(asset.FormSchemaJson), budget, depth, child.WorkflowKindKey == "approval"); return true;
    }

    /// <summary>子预算从父额度预留且不重复分配，嵌套实例不能把每根4096额度相乘。</summary>
    public static bool TryReadBudget(WorkflowDefinitionDraft draft, out int budget, out int depth)
    {
        budget = 0; depth = 0;
        if (draft.Nodes is null || draft.Nodes.Count is < 2 or > WorkflowDefinitionCompiler.MaximumNodes ||
            draft.Nodes.Any(node => node is null || node.Config.ValueKind != JsonValueKind.Object)) return false;
        // 异步屏障与汇合到达也占真实步骤；保守预留，不能把设计节点数当作实际执行次数。
        var edges = draft.Nodes.Sum(node =>
            (node.Config.TryGetProperty("nextNodeKeys", out var next) && next.ValueKind == JsonValueKind.Array ? next.GetArrayLength() : 0) +
            (node.Config.TryGetProperty("branches", out var branches) && branches.ValueKind == JsonValueKind.Array ? branches.GetArrayLength() : 0) +
            (node.Config.TryGetProperty("defaultNextNodeKey", out _) ? 1 : 0));
        if (edges > WorkflowDefinitionCompiler.MaximumEdges) return false;
        budget = 2 * draft.Nodes.Count + edges; depth = 1;
        foreach (var node in draft.Nodes.Where(node => node.NodeTypeKey == WorkflowSubprocessPolicy.NodeType))
        {
            if (!WorkflowSubprocessPolicy.TryRead(node.Config, null, out var policy) || !node.Config.TryGetProperty("boundSubprocess", out var data) ||
                !WorkflowBoundSubprocess.TryRead(data, policy!.DefinitionVersionId, out var bound) ||
                budget > WorkflowActivityScheduler.InstanceActivityBudget - bound!.ActivityBudget * (policy.ExecutionSet?.MaximumCount ?? 1)) return false;
            budget += bound.ActivityBudget * (policy.ExecutionSet?.MaximumCount ?? 1); depth = Math.Max(depth, 1 + bound.MaximumDepth);
        }
        budget = Math.Max(64, budget);
        return budget <= WorkflowActivityScheduler.InstanceActivityBudget && depth <= WorkflowSubprocessPolicy.MaximumDepth;
    }
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static Result<WorkflowDefinitionDraft> Invalid() => Result<WorkflowDefinitionDraft>.Failure(new(
        WorkflowErrorCodes.DefinitionSubprocessInvalid, "Choose an available child version and explicitly map compatible fields within the execution budget.", ErrorType.Validation));
}
