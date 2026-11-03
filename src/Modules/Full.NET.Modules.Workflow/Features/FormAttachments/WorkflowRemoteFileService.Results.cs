using System.Text.Json;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>目标本地文件反向签发；返回原输入能力时仅原签发方可解析自己的文件，不能再委托第三方。</summary>
internal sealed partial class WorkflowRemoteFileService
{
    internal async Task<WorkflowPreparedTargetProjection?> PrepareTargetResultAsync(WorkflowCollaborationAdmissionRecord admission, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        var agreement = await AgreementAsync(admission.Id, scope.TenantScopeKey, true, token).ConfigureAwait(false);
        if (agreement is null || agreement.Admission.BindingHash != admission.BindingHash || agreement.Admission.TargetInstanceId is not { } target ||
            agreement.Admission.StatusKey is not ("created" or "active")) return null;
        var root = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            P(("Id", target), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (root?.StatusKey != "completed" || root.DefinitionVersionId != agreement.Binding.TargetDefinitionVersionId ||
            !await WorkflowCollaborationRuntimeGate.AllowsAsync(root.Id, scope, null, clock.UtcNow, query, token, tenants: tenants).ConfigureAwait(false) ||
            await query.QuerySingleOrDefaultAsync<int>(WorkflowCollaborationControlSql.CountUnsettled,
                P(("TargetInstanceId", root.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 0) return null;
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            P(("DefinitionVersionId", root.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var submission = await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            P(("InstanceId", root.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null || submission is null) return null;
        WorkflowFormSchema? schema; Dictionary<string, JsonElement>? values;
        try
        {
            schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            values = JsonSerializer.Deserialize(submission.SubmissionJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        }
        catch (JsonException) { return null; }
        if (schema is null || values is null || !WorkflowFormValueValidator.Validate(schema, values)) return null;
        var fields = schema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var projected = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var mapping in agreement.Accepted.OutputFields)
        {
            if (!fields.TryGetValue(mapping.Key, out var field) || !WorkflowCollaborationValueProjection.Supports(field, true)) return null;
            if (!values.TryGetValue(mapping.Key, out var value)) continue;
            if (!WorkflowCollaborationValueProjection.Valid(field, value, true)) return null;
            if (field.FieldTypeKey != "attachment" || value.ValueKind == JsonValueKind.Null) { projected.Add(mapping.Value, value.Clone()); continue; }
            if (!WorkflowFormAttachmentValueRules.TryReadFileIds(value, out var ids)) return null;
            var result = new List<Guid>();
            foreach (var id in ids)
            {
                var remote = await query.QuerySingleOrDefaultAsync<WorkflowRemoteFileRecord>(WorkflowCollaborationFileSql.FindMappedRemote,
                    P(("InstanceId", root.Id), ("TenantScopeKey", scope.TenantScopeKey), ("FieldKey", mapping.Key), ("FileId", id)), token).ConfigureAwait(false);
                if (remote is not null)
                {
                    var reusable = await ReadForReuseAsync(root.Id, mapping.Key, mapping.Key, id, scope.TenantScopeKey, token).ConfigureAwait(false);
                    if (reusable?.CallId != admission.Id || reusable.Capability.DirectionKey != "input") return null;
                    result.Add(id); continue;
                }
                var reference = await query.QuerySingleOrDefaultAsync<WorkflowFileIssuingReference>(WorkflowCollaborationFileSql.IssuingReference,
                    P(("InstanceId", root.Id), ("TenantScopeKey", scope.TenantScopeKey), ("FieldKey", mapping.Key), ("FileId", id)), token).ConfigureAwait(false);
                if (reference?.OwnerReferenceId is not { } owner || reference.FieldKey != mapping.Key) return null;
                var issued = await files.IssueAsync(new(admission.Id, "result", mapping.Value, id, owner, agreement.Binding.SourceTenantId, agreement.Grant.ExpiresAtUtc), token).ConfigureAwait(false);
                if (!issued.IsSuccess) return null; result.Add(issued.Value!.Id);
            }
            projected.Add(mapping.Value, FileIds(result));
        }
        var json = JsonSerializer.Serialize(projected, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        return System.Text.Encoding.UTF8.GetByteCount(json) > 131_072 ? null : new(root.Id, submission.Revision,
            WorkflowCollaborationCallPolicy.Hash(submission.SubmissionJson), json);
    }

    internal async Task<WorkflowPreparedSourceFileResult?> PrepareSourceResultAsync(WorkflowCollaborationAdmissionRecord admission,
        WorkflowCollaborationAcceptancePolicy accepted, WorkflowFormSchema schema, string projection, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        var agreement = await AgreementAsync(admission.Id, scope.TenantScopeKey, false, token).ConfigureAwait(false);
        if (agreement is null || agreement.Admission.StatusKey != "active" || agreement.Admission.BindingHash != admission.BindingHash) return null;
        Dictionary<string, JsonElement>? values;
        try
        {
            using var document = JsonDocument.Parse(projection);
            if (!WorkflowVotePolicy.UniqueObject(document.RootElement)) return null;
            values = JsonSerializer.Deserialize(document.RootElement, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        }
        catch (JsonException) { return null; }
        if (values is null || values.Keys.Any(key => !accepted.OutputFields.Values.Contains(key, StringComparer.Ordinal))) return null;
        var fields = schema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var prepared = new List<WorkflowPreparedRemoteAttachment>(); var local = new Dictionary<Guid, WorkflowAttachmentReuseSource>();
        foreach (var pair in values.ToArray())
        {
            if (!fields.TryGetValue(pair.Key, out var field)) return null;
            if (field.FieldTypeKey != "attachment" || pair.Value.ValueKind == JsonValueKind.Null) continue;
            if (!WorkflowFormAttachmentValueRules.TryReadFileIds(pair.Value, out var ids)) return null;
            var projected = new List<Guid>();
            foreach (var id in ids)
            {
                var own = await files.ResolveIssuedAsync(id, admission.Id, token).ConfigureAwait(false);
                if (own is not null)
                {
                    if (own.Capability.DirectionKey != "input" || !Accepts(field, own.Capability, agreement.Grant.ExpiresAtUtc)) return null;
                    var sourceField = accepted.InputFields.SingleOrDefault(mapping => mapping.Value == own.Capability.FieldKey).Key;
                    if (sourceField is null) return null;
                    local.TryAdd(own.FileId, new(agreement.Binding.SourceInstanceId, sourceField)); projected.Add(own.FileId); continue;
                }
                var capability = await files.DescribeAsync(id, admission.Id, token).ConfigureAwait(false);
                if (capability is null || capability.DirectionKey != "result" || capability.FieldKey != pair.Key ||
                    !Accepts(field, capability, agreement.Grant.ExpiresAtUtc)) return null;
                prepared.Add(new(admission.Id, scope.TenantScopeKey, pair.Key, capability)); projected.Add(id);
            }
            values[pair.Key] = FileIds(projected);
        }
        return new(JsonSerializer.Serialize(values, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement), new(prepared), local);
    }

    private static JsonElement FileIds(IEnumerable<Guid> values)
    {
        var json = WorkflowJsonCanonicalizer.Compile(writer => { writer.WriteStartArray(); foreach (var value in values) writer.WriteStringValue(value); writer.WriteEndArray(); });
        using var document = JsonDocument.Parse(json.CanonicalJson); return document.RootElement.Clone();
    }
}

/// <summary>事务外签发完成的最小投影；目标事务必须复核相同表单修订和摘要。</summary>
internal sealed record WorkflowPreparedTargetProjection(Guid InstanceId, long SubmissionRevision, string SubmissionHash, string Json);
/// <summary>接纳前已验证的反向能力或原签发方本地引用。</summary>
internal sealed record WorkflowPreparedSourceFileResult(string ProjectionJson, WorkflowRemoteAttachmentContext Remote,
    IReadOnlyDictionary<Guid, WorkflowAttachmentReuseSource> LocalReuse);
