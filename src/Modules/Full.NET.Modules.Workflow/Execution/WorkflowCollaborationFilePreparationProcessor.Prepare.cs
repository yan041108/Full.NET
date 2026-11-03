using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Workflow.Execution;

internal sealed partial class WorkflowCollaborationFilePreparationProcessor
{
    private static async Task<string?> PrepareAsync(IServiceProvider services, PreparationState value, CancellationToken token)
    {
        if (value.Call.StatusKey != "preparing") return null;
        var query = services.GetRequiredService<IQueryExecutor>();
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            P(("DefinitionVersionId", value.Root.DefinitionVersionId), ("TenantScopeKey", value.Call.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null || asset.FormVersionId != value.Binding.SourceFormVersionId) return null;
        WorkflowFormSchema? schema; Dictionary<string, JsonElement>? original;
        try
        {
            schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            original = JsonSerializer.Deserialize(value.Call.InputJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        }
        catch (JsonException) { return null; }
        if (schema is null || original is null || !WorkflowCollaborationCallPolicy.TryProject(schema, value.Offer, value.Accepted, original, out var localProjection, preparingFiles: true) ||
            localProjection != value.Call.ProjectionJson) return null;
        var fields = schema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var projected = JsonSerializer.Deserialize(localProjection, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement)!;
        var files = services.GetRequiredService<IWorkflowFileReadCapabilityStore>();
        foreach (var mapping in value.Accepted.InputFields)
        {
            if (fields[mapping.Key].FieldTypeKey != "attachment" || !original.TryGetValue(mapping.Key, out var data) || data.ValueKind == JsonValueKind.Null) continue;
            if (!WorkflowFormAttachmentValueRules.TryReadFileIds(data, out var originalFiles)) return null;
            var capabilities = new List<Guid>();
            foreach (var file in originalFiles)
            {
                if (await query.QuerySingleOrDefaultAsync<WorkflowRemoteFileRecord>(WorkflowCollaborationFileSql.FindMappedRemote,
                        P(("InstanceId", value.Root.Id), ("TenantScopeKey", value.Call.TenantScopeKey), ("FieldKey", mapping.Key), ("FileId", file)), token).ConfigureAwait(false) is not null) return null;
                var reference = await query.QuerySingleOrDefaultAsync<WorkflowFileIssuingReference>(WorkflowCollaborationFileSql.IssuingReference,
                    P(("InstanceId", value.Root.Id), ("TenantScopeKey", value.Call.TenantScopeKey), ("FieldKey", mapping.Key), ("FileId", file)), token).ConfigureAwait(false);
                if (reference?.OwnerReferenceId is not { } owner || reference.FieldKey != mapping.Key) return null;
                var issued = await files.IssueAsync(new(value.Call.Id, "input", mapping.Value, file, owner, value.Binding.TargetTenantId, value.Grant.ExpiresAtUtc), token).ConfigureAwait(false);
                if (!issued.IsSuccess) return null;
                var capability = issued.Value!;
                if (capability.CallId != value.Call.Id || capability.DirectionKey != "input" || capability.FieldKey != mapping.Value || capability.ExpiresAtUtc > value.Grant.ExpiresAtUtc) return null;
                capabilities.Add(capability.Id);
            }
            var json = WorkflowJsonCanonicalizer.Compile(writer => { writer.WriteStartArray(); foreach (var id in capabilities) writer.WriteStringValue(id); writer.WriteEndArray(); });
            using var document = JsonDocument.Parse(json.CanonicalJson); projected[mapping.Value] = document.RootElement.Clone();
        }
        var result = JsonSerializer.Serialize(projected, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        return System.Text.Encoding.UTF8.GetByteCount(result) <= 131_072 ? result : null;
    }
}
