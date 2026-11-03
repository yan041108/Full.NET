using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.ManageCollaborationGrants;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>只有Relay从耐久共享准入读取此上下文；公共HTTP和普通业务端口不能声明该身份。</summary>
internal sealed record WorkflowCollaborationStartContext(WorkflowCollaborationAdmissionRecord Admission,
    WorkflowCollaborationCallBinding Binding, WorkflowCollaborationAcceptancePolicy Acceptance)
{
    internal const string BusinessType = "workflow.collaboration";
    internal string IdempotencyKey => "workflow-collaboration-" + Admission.Id.ToString("N");
}

/// <summary>验证共享最小绑定，不把自报Tenant或摘要当作身份签名。</summary>
internal static class WorkflowCollaborationAdmissionProof
{
    public static bool TryRead(WorkflowCollaborationAdmissionRecord row, out WorkflowCollaborationCallBinding? binding)
    {
        binding = null;
        if (Encoding.UTF8.GetByteCount(row.ProjectionJson) > 131_072 || row.ProjectionHash != WorkflowCollaborationCallPolicy.Hash(row.ProjectionJson)) return false;
        return TryReadBinding(new(row.Id, row.SourceTenantScopeKey, row.TargetTenantScopeKey, row.DeliveryId, row.GrantId, row.BindingJson,
            row.BindingHash, row.ProjectionHash, row.StatusKey, row.TargetInstanceId, row.Revision), out binding);
    }

    /// <summary>只证明读取所属的调用上下文；业务执行仍须调用TryRead验证完整投影正文。</summary>
    internal static bool TryReadBinding(WorkflowCollaborationListCandidate row, out WorkflowCollaborationCallBinding? binding)
    {
        binding = null;
        if (row.Id == Guid.Empty || row.DeliveryId == Guid.Empty || row.GrantId == Guid.Empty || row.Revision < 1 ||
            row.BindingJson.Length > 16_384 || !WorkflowCollaborationAcceptancePolicy.Digest(row.ProjectionHash) ||
            row.BindingHash != WorkflowCollaborationCallPolicy.Hash(row.BindingJson)) return false;
        try
        {
            using var json = JsonDocument.Parse(row.BindingJson);
            if (!WorkflowVotePolicy.UniqueObject(json.RootElement) || json.RootElement.EnumerateObject().Count() != 24) return false;
            binding = JsonSerializer.Deserialize(json.RootElement, WorkflowJsonSerializerContext.Default.WorkflowCollaborationCallBinding);
            return binding is not null && binding.CallId == row.Id && binding.DeliveryId == row.DeliveryId && binding.GrantId == row.GrantId &&
                binding.SourceInstanceId != Guid.Empty && binding.ExecutionEpochId != Guid.Empty && binding.SourceDefinitionVersionId != Guid.Empty &&
                binding.SourceFormVersionId != Guid.Empty && binding.TargetDefinitionVersionId != Guid.Empty && binding.InputFormRevision > 0 &&
                binding.SourceTenantId != Guid.Empty && binding.TargetTenantId != Guid.Empty && binding.SourceTenantId != binding.TargetTenantId &&
                binding.SourceTenantScopeKey == row.SourceTenantScopeKey && binding.TargetTenantScopeKey == row.TargetTenantScopeKey &&
                binding.SourceTenantScopeKey == WorkflowCollaborationGrantPolicy.ScopeKey(binding.SourceTenantId) &&
                binding.TargetTenantScopeKey == WorkflowCollaborationGrantPolicy.ScopeKey(binding.TargetTenantId) &&
                binding.ProjectionHash == row.ProjectionHash && binding.GrantRevision == 2 && binding.Depth is >= 1 and <= 8 &&
                binding.ReservedActivities is >= 2 and <= 4096 && WorkflowCollaborationAcceptancePolicy.Digest(binding.PolicyHash) &&
                WorkflowCollaborationAcceptancePolicy.Digest(binding.TargetSummaryHash) && WorkflowCollaborationAcceptancePolicy.Digest(binding.InputHash) &&
                WorkflowNodeKeyValidator.IsValid(binding.NodeKey) && WorkflowNodeKeyValidator.IsValid(binding.ContinuationNodeKey) &&
                binding.ParallelJoinId != Guid.Empty && (binding.ParallelJoinId is null) == (binding.ParallelBranchKey is null);
        }
        catch (JsonException) { binding = null; return false; }
    }
}

/// <summary>锁共享授权及准入后只写目标根；来源、预算先于第一次真实活动落盘。</summary>
internal static class WorkflowCollaborationStartGuard
{
    public static async Task<Result<bool>> AcquireAsync(WorkflowCollaborationStartContext context, StartWorkflowInstanceRequest request,
        Guid actor, WorkflowRuntimeAssetRecord asset, WorkflowManagementScope scope, IQueryExecutor query, ICommandExecutor commands,
        DateTimeOffset now, CancellationToken token)
    {
        var proof = context.Binding;
        if (!WorkflowCollaborationAdmissionProof.TryRead(context.Admission, out var prepared) || prepared != proof ||
            proof.TargetTenantScopeKey != scope.TenantScopeKey || proof.TargetTenantId != scope.TenantId || actor != context.Acceptance.StarterUserId ||
            request.BusinessType != WorkflowCollaborationStartContext.BusinessType || request.BusinessId != proof.CallId.ToString("D") ||
            request.IdempotencyKey != context.IdempotencyKey || request.BusinessTitle is not null || request.DefinitionVersionId != proof.TargetDefinitionVersionId ||
            request.InitialValues.GetRawText() != context.Admission.ProjectionJson || request.AssigneeSelections?.Count > 0) return Invalid();
        var parameters = WorkflowSqlParameters.Create(("Id", proof.CallId), ("TenantScopeKey", scope.TenantScopeKey),
            ("SourceTenantScopeKey", proof.SourceTenantScopeKey), ("GrantId", proof.GrantId), ("GrantRevision", proof.GrantRevision),
            ("PolicyHash", proof.PolicyHash), ("BindingHash", context.Admission.BindingHash), ("Now", now));
        if (await commands.ExecuteAsync(WorkflowCollaborationRelaySql.LockGrant, parameters, token).ConfigureAwait(false) != 1 ||
            await commands.ExecuteAsync(WorkflowCollaborationRelaySql.LockAdmission, parameters, token).ConfigureAwait(false) != 1) return Invalid();
        var row = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission, parameters, token).ConfigureAwait(false);
        var grant = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find,
            WorkflowSqlParameters.Create(("Id", proof.GrantId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (row is null || row.StatusKey != "requested" || row.TargetInstanceId is not null || row.BindingHash != context.Admission.BindingHash ||
            !WorkflowCollaborationAdmissionProof.TryRead(row, out var actual) || actual != proof || grant is null ||
            !WorkflowCollaborationGrantPolicy.TryAuthorize(grant, scope.TenantScopeKey, true, proof.GrantRevision, now, out _, out var acceptance) ||
            grant.PolicyHash != proof.PolicyHash || acceptance!.TargetSummaryHash != proof.TargetSummaryHash || acceptance.MaximumActivities != proof.ReservedActivities ||
            JsonSerializer.Serialize(acceptance, WorkflowJsonSerializerContext.Default.WorkflowCollaborationAcceptancePolicy) !=
                JsonSerializer.Serialize(context.Acceptance, WorkflowJsonSerializerContext.Default.WorkflowCollaborationAcceptancePolicy)) return Invalid();
        var summary = JsonSerializer.Deserialize(grant.TargetSummaryJson!, WorkflowJsonSerializerContext.Default.WorkflowCollaborationTargetSummary);
        return summary is not null && asset.DefinitionVersionId == summary.DefinitionVersionId &&
            WorkflowCollaborationCallPolicy.Hash(asset.CanonicalJson) == summary.DefinitionContentHash &&
            WorkflowCollaborationCallPolicy.Hash(asset.FormSchemaJson) == summary.FormContentHash
            ? Result<bool>.Success(true) : Invalid();
    }

    public static async Task<Result<bool>> LinkAsync(WorkflowCollaborationStartContext context, Guid instanceId,
        WorkflowManagementScope scope, DateTimeOffset now, ICommandExecutor commands, CancellationToken token)
    {
        var parameters = WorkflowSqlParameters.Create(("Id", context.Binding.CallId), ("CallId", context.Binding.CallId),
            ("InstanceId", instanceId), ("TargetInstanceId", instanceId), ("RootInstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey),
            ("BindingHash", context.Admission.BindingHash), ("Depth", context.Binding.Depth), ("Granted", context.Binding.ReservedActivities), ("Now", now));
        if (await commands.ExecuteAsync(WorkflowCollaborationRelaySql.MarkCreated, parameters, token).ConfigureAwait(false) != 1 ||
            await commands.ExecuteAsync(WorkflowCollaborationRelaySql.InsertOrigin, parameters, token).ConfigureAwait(false) != 1 ||
            await commands.ExecuteAsync(WorkflowSubprocessSql.InsertBudget, parameters, token).ConfigureAwait(false) != 1) return Invalid();
        return Result<bool>.Success(true);
    }
    private static Result<bool> Invalid() => Result<bool>.Failure(new(WorkflowErrorCodes.CollaborationGrantConflict,
        "The fixed collaboration admission is no longer valid.", ErrorType.Conflict));
}
