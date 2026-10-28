using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>文件投影只派生最小复用来源；准备不建立子根，父锁内最终启动仍须复核同一项。</summary>
internal static class WorkflowSubprocessAttachmentProjection
{
    public static async Task<IReadOnlyDictionary<Guid, WorkflowAttachmentReuseSource>?> PrepareInputAsync(WorkflowChildStartContext context,
        StartWorkflowInstanceRequest request, Guid actor, string scope, WorkflowFormSchema childSchema, IQueryExecutor query, CancellationToken token)
    {
        if (!WorkflowFormAttachmentValueRules.ExtractAttachmentFields(childSchema, request.InitialValues.EnumerateObject()
            .ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal)).Any(pair => pair.Value.Length > 0))
            return new Dictionary<Guid, WorkflowAttachmentReuseSource>();
        var parameters = WorkflowSqlParameters.Create(("Id", context.CallId), ("InstanceId", context.ParentInstanceId), ("TenantScopeKey", scope));
        var call = await query.QuerySingleOrDefaultAsync<WorkflowSubprocessCallRecord>(WorkflowSubprocessSql.Find, parameters, token).ConfigureAwait(false);
        var item = await query.QuerySingleOrDefaultAsync<WorkflowSubprocessItemRecord>(WorkflowSubprocessSql.FindItem,
            WorkflowSqlParameters.Create(("Id", context.ItemId), ("CallId", context.CallId), ("InstanceId", context.ParentInstanceId), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        var parent = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", context.ParentInstanceId), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        if (call is null || item is null || parent?.StatusKey != "active" || call.CancellationRequested || call.ErrorCode is not null ||
            call.StatusKey is not ("requested" or "running") || call.ChildDefinitionVersionId != request.DefinitionVersionId ||
            item.StatusKey != "pending" || item.ChildInstanceId is not null || item.StarterUserId != actor ||
            item.InputHash != context.InputHash || WorkflowSubprocessBindings.Hash(item.InputJson) != context.InputHash ||
            item.InputJson != request.InitialValues.GetRawText()) return null;
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", parent.DefinitionVersionId), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        if (asset is null) return null;
        try
        {
            var draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
            var schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            var original = JsonSerializer.Deserialize(call.ParentInputJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            var node = draft?.Nodes.SingleOrDefault(node => node.NodeKey == call.NodeKey);
            if (node?.NodeTypeKey != WorkflowSubprocessPolicy.NodeType || node.Config.GetRawText() != call.PolicyJson || schema is null || original is null ||
                !WorkflowSubprocessPolicy.TryRead(node.Config, schema, out var policy) || !policy.MatchesSchemas(schema, childSchema)) return null;
            WorkflowMultiInstanceSeed? seed = null;
            if (policy.ExecutionSet is { } multi)
            {
                var set = await query.QuerySingleOrDefaultAsync<WorkflowMultiInstanceSetRecord>(WorkflowMultiInstanceSql.Find,
                    WorkflowSqlParameters.Create(("CallId", call.Id), ("InstanceId", call.InstanceId), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
                if (set?.StatusKey != "running" || !WorkflowMultiInstanceSnapshotRules.TryRead(set, multi, original, out var snapshot)) return null;
                seed = snapshot.Source.Items.SingleOrDefault(seed => seed.ItemKey == item.ItemKey && seed.SequenceNo == item.SequenceNo);
                if (seed is null) return null;
                if (policy.Starter is { SourceKey: not "parent" } && (snapshot.Starters?.ByItemKey is not { } starters || !starters.TryGetValue(seed.ItemKey, out var captured) || captured != actor)) return null;
            }
            var expectedStarter = parent.StartedById;
            if (policy.Starter is not null && !policy.Starter.TryResolve(parent.StartedById, original, seed ?? new("single", 1, "1"), out expectedStarter) || actor != expectedStarter) return null;
            if (!policy.TryCreateInput(original, childSchema, out var expected, seed) || expected != item.InputJson) return null;
            var sources = new Dictionary<Guid, WorkflowAttachmentReuseSource>();
            var childFields = childSchema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
            foreach (var mapping in policy.InputFields)
            {
                if (childFields[mapping.Key].FieldTypeKey != "attachment" || !original.TryGetValue(mapping.Value, out var value) || value.ValueKind == JsonValueKind.Null) continue;
                if (!WorkflowFormAttachmentValueRules.TryReadFileIds(value, out var fileIds)) return null;
                foreach (var file in fileIds) sources.TryAdd(file, new(parent.Id, mapping.Value));
            }
            return sources;
        }
        catch (JsonException) { return null; }
    }

    public static IReadOnlyDictionary<Guid, WorkflowAttachmentReuseSource>? PrepareOutput(WorkflowSubprocessPolicy policy, WorkflowFormSchema parentSchema,
        IReadOnlyDictionary<string, JsonElement> patch, IReadOnlyList<WorkflowSubprocessItemRecord> selected)
    {
        var sources = new Dictionary<Guid, WorkflowAttachmentReuseSource>();
        var files = WorkflowFormAttachmentValueRules.ExtractAttachmentFields(parentSchema, patch);
        foreach (var (field, ids) in files)
        {
            if (ids.Length == 0) continue;
            if (!policy.OutputFields.TryGetValue(field, out var childField)) return null;
            foreach (var id in ids)
            {
                WorkflowAttachmentReuseSource? found = null;
                foreach (var item in selected.OrderBy(item => item.ItemKey, StringComparer.Ordinal))
                {
                    if (item.ChildInstanceId is null || item.StatusKey != "succeeded" || item.ResultJson is null || item.ResultHash != WorkflowSubprocessBindings.Hash(item.ResultJson)) return null;
                    try
                    {
                        var values = JsonSerializer.Deserialize(item.ResultJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
                        if (values is not null && values.TryGetValue(field, out var value) && WorkflowFormAttachmentValueRules.TryReadFileIds(value, out var childFiles) && childFiles.Contains(id))
                        { found = new(item.ChildInstanceId.Value, childField); break; }
                    }
                    catch (JsonException) { return null; }
                }
                if (found is null) return null;
                sources.TryAdd(id, found);
            }
        }
        return sources;
    }
}
