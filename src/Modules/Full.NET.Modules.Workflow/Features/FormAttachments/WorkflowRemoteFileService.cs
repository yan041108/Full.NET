using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>只由已验证的协议或明确本地映射产生；客户端不能提交此准备上下文。</summary>
internal sealed record WorkflowPreparedRemoteAttachment(Guid CallId, string TenantScopeKey, string LocalFieldKey, WorkflowFileReadCapability Capability);
internal sealed record WorkflowRemoteAttachmentContext(IReadOnlyList<WorkflowPreparedRemoteAttachment> Attachments)
{
    internal WorkflowPreparedRemoteAttachment? Find(string field, Guid id) => Attachments.SingleOrDefault(item => item.LocalFieldKey == field && item.Capability.Id == id);
}

/// <summary>先复核Workflow当前授权与真实关联，再让Files按能力核对原持有；不把外文件伪装为本地claim。</summary>
internal sealed partial class WorkflowRemoteFileService(IQueryExecutor query, ICurrentTenant tenant, IClock clock,
    IActiveTenantContextResolver tenants, IWorkflowFileReadCapabilityStore files)
{
    internal async Task<WorkflowRemoteAttachmentContext?> PrepareInputAsync(WorkflowCollaborationStartContext context, WorkflowFormSchema schema,
        IReadOnlyDictionary<string, JsonElement> values, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (scope.TenantScopeKey != context.Binding.TargetTenantScopeKey ||
            await AgreementAsync(context.Admission.Id, scope.TenantScopeKey, true, token).ConfigureAwait(false) is not { } agreement ||
            agreement.Admission.BindingHash != context.Admission.BindingHash || agreement.Admission.StatusKey != "requested") return null;
        var prepared = new List<WorkflowPreparedRemoteAttachment>();
        foreach (var field in schema.Sections.SelectMany(section => section.Fields).Where(field => field.FieldTypeKey == "attachment"))
        {
            if (!values.TryGetValue(field.FieldKey, out var value) || value.ValueKind == JsonValueKind.Null) continue;
            if (!WorkflowFormAttachmentValueRules.TryReadFileIds(value, out var ids) || !agreement.Accepted.InputFields.Values.Contains(field.FieldKey, StringComparer.Ordinal)) return null;
            foreach (var id in ids)
            {
                var capability = await files.DescribeAsync(id, context.Admission.Id, token).ConfigureAwait(false);
                if (capability is null || capability.DirectionKey != "input" || capability.FieldKey != field.FieldKey ||
                    !Accepts(field, capability, agreement.Grant.ExpiresAtUtc)) return null;
                prepared.Add(new(context.Admission.Id, scope.TenantScopeKey, field.FieldKey, capability));
            }
        }
        return new(prepared);
    }

    internal async Task<WorkflowPreparedRemoteAttachment?> ReadForReuseAsync(Guid instance, string? sourceField, string destinationField,
        Guid file, string scope, CancellationToken token)
    {
        if (WorkflowManagementScope.Resolve(tenant).TenantScopeKey != scope) return null;
        if (sourceField is not null)
        {
            var exact = await query.QuerySingleOrDefaultAsync<WorkflowFileIssuingReference>(WorkflowCollaborationFileSql.IssuingReference,
                P(("InstanceId", instance), ("FieldKey", sourceField), ("FileId", file), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
            if (exact?.FieldKey != sourceField) return null;
        }
        var row = await query.QuerySingleOrDefaultAsync<WorkflowRemoteFileRecord>(sourceField is null ? WorkflowCollaborationFileSql.FindRemote : WorkflowCollaborationFileSql.FindMappedRemote,
            P(("InstanceId", instance), ("FieldKey", sourceField), ("FileId", file), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        if (row is null || !await AllowsAsync(row, token).ConfigureAwait(false)) return null;
        var descriptor = await files.DescribeAsync(file, row.CallId, token).ConfigureAwait(false);
        return descriptor is null || !Same(row, descriptor) ? null : new(row.CallId, scope, destinationField, descriptor);
    }

    internal async Task<Result<HostFileContent>?> OpenAsync(Guid instance, Guid file, string scope, CancellationToken token)
    {
        var row = await query.QuerySingleOrDefaultAsync<WorkflowRemoteFileRecord>(WorkflowCollaborationFileSql.FindRemote,
            P(("InstanceId", instance), ("FileId", file), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        if (row is null) return null;
        if (WorkflowManagementScope.Resolve(tenant).TenantScopeKey != scope || !await AllowsAsync(row, token).ConfigureAwait(false))
            return Failure(ErrorType.Forbidden);
        var content = await files.OpenAsync(file, row.CallId, token).ConfigureAwait(false);
        if (!content.IsSuccess) return Failure(ErrorType.NotFound);
        if (!Same(row, content.Value!.Capability)) { await content.Value.Content.DisposeAsync().ConfigureAwait(false); return Failure(ErrorType.Forbidden); }
        return Result<HostFileContent>.Success(new(content.Value.Content, content.Value.Capability.ContentType, content.Value.Capability.OriginalFileName));
    }

    private async Task<bool> AllowsAsync(WorkflowRemoteFileRecord row, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (scope.TenantScopeKey != row.TenantScopeKey) return false;
        var agreement = await AgreementAsync(row.CallId, scope.TenantScopeKey, row.DirectionKey == "input", token).ConfigureAwait(false);
        if (agreement is null) return false;
        if (row.DirectionKey == "input") return agreement.Binding.TargetTenantScopeKey == scope.TenantScopeKey &&
            agreement.Accepted.InputFields.Values.Contains(row.CapabilityFieldKey, StringComparer.Ordinal) &&
            await WorkflowCollaborationRuntimeGate.AllowsReadAsync(row.InstanceId, scope, null, clock.UtcNow, query, token, tenants).ConfigureAwait(false);
        if (row.DirectionKey != "result" || agreement.Binding.SourceTenantScopeKey != scope.TenantScopeKey || agreement.Admission.StatusKey != "completed" ||
            !agreement.Accepted.OutputFields.Values.Contains(row.CapabilityFieldKey, StringComparer.Ordinal)) return false;
        var call = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationCallRecord>(WorkflowCollaborationCallSql.Find,
            P(("Id", row.CallId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (call?.StatusKey != "completed" || call.BindingHash != agreement.Admission.BindingHash) return false;
        var cursor = row.InstanceId; var seen = new HashSet<Guid>();
        for (var depth = 0; depth <= WorkflowSubprocessPolicy.MaximumDepth && seen.Add(cursor); depth++)
        {
            if (cursor == agreement.Binding.SourceInstanceId) return true;
            var parent = await query.QuerySingleOrDefaultAsync<Guid?>(WorkflowCollaborationFileSql.FindParent,
                P(("InstanceId", cursor), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (parent is not { } id || id == Guid.Empty) return false; cursor = id;
        }
        return false;
    }

    internal async Task<Agreement?> AgreementAsync(Guid id, string scope, bool target, CancellationToken token)
    {
        var admission = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission,
            P(("Id", id), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        if (admission is null || admission.StatusKey is not ("requested" or "created" or "active" or "completed") ||
            !WorkflowCollaborationAdmissionProof.TryRead(admission, out var proof) || (target ? proof!.TargetTenantScopeKey : proof!.SourceTenantScopeKey) != scope) return null;
        var grant = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find,
            P(("Id", proof.GrantId), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        if (grant is null || grant.PolicyHash != proof.PolicyHash || !WorkflowCollaborationGrantPolicy.TryAuthorize(grant, scope, target, proof.GrantRevision,
                clock.UtcNow, out _, out var accepted) ||
            proof.SourceTenantId is { } source && await tenants.ResolveActiveByIdAsync(source, token).ConfigureAwait(false) is null ||
            proof.TargetTenantId is { } destination && await tenants.ResolveActiveByIdAsync(destination, token).ConfigureAwait(false) is null) return null;
        return new(admission, proof, grant, accepted!);
    }

    internal static bool Accepts(WorkflowFormField field, WorkflowFileReadCapability value, DateTimeOffset expiry) =>
        value.ExpiresAtUtc <= expiry && WorkflowFormAttachmentConstraints.TryRead(field, out _, out var maximum, out var extensions) &&
        value.SizeBytes > 0 && value.SizeBytes <= maximum && WorkflowFormAttachmentValueRules.MatchesAllowedExtension(value.OriginalFileName, extensions);
    internal static bool Same(WorkflowRemoteFileRecord row, WorkflowFileReadCapability value) => row.CallId == value.CallId && row.CapabilityId == value.Id &&
        row.DirectionKey == value.DirectionKey && row.CapabilityFieldKey == value.FieldKey && row.OriginalFileName == value.OriginalFileName &&
        row.ContentType == value.ContentType && row.SizeBytes == value.SizeBytes && row.ContentHash == value.ContentHash && row.ExpiresAtUtc == value.ExpiresAtUtc;
    private static Result<HostFileContent> Failure(ErrorType type) => Result<HostFileContent>.Failure(new(WorkflowErrorCodes.FormAttachmentInvalid, "The shared workflow file is unavailable.", type));
    private static Dictionary<string, object?> P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    internal sealed record Agreement(WorkflowCollaborationAdmissionRecord Admission, WorkflowCollaborationCallBinding Binding,
        WorkflowCollaborationGrantFact Grant, WorkflowCollaborationAcceptancePolicy Accepted);
}
