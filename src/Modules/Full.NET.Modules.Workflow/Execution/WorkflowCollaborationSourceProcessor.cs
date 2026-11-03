using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Features.FormAttachments;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>只在源自己的作用域接纳已认证目标事实；外模块检查与文件准备在业务事务外完成。</summary>
internal sealed partial class WorkflowCollaborationSourceProcessor(IQueryExecutor query, ICommandExecutor commands, ICommandTransaction transaction,
    ICurrentTenant tenant, IActiveTenantContextResolver tenants, IClock clock, IIdGenerator ids,
    WorkflowAutomaticOutputCoordinator outputs, WorkflowActivityScheduler scheduler, WorkflowRemoteFileService remoteFiles)
{
    internal async Task<bool> ProcessAsync(WorkflowCollaborationAdmissionRecord admission, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (scope.TenantScopeKey != admission.SourceTenantScopeKey || !WorkflowCollaborationAdmissionProof.TryRead(admission, out var proof)) return false;
        var changed = false;
        if (admission.StatusKey != "cancel_requested" &&
            (proof!.SourceTenantId is { } source && await tenants.ResolveActiveByIdAsync(source, token).ConfigureAwait(false) is null ||
             proof!.TargetTenantId is { } target && await tenants.ResolveActiveByIdAsync(target, token).ConfigureAwait(false) is null)) return false;
        if (admission.StatusKey == "created") changed = await ConfirmCreatedAsync(admission, token).ConfigureAwait(false);
        var current = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission,
            P(("Id", admission.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (current is null) return changed;
        if (current.StatusKey == "active") changed |= await ApplyCompletionAsync(current, token).ConfigureAwait(false);
        else if (current.StatusKey == "cancel_requested") changed |= await ApplyCancellationAsync(current, token).ConfigureAwait(false);
        return changed;
    }

    private async Task<SourceState?> ReadAsync(WorkflowCollaborationAdmissionRecord prepared, string kind, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        var parameters = P(("Id", prepared.Id), ("KindKey", kind), ("TenantScopeKey", scope.TenantScopeKey));
        var admission = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission, parameters, token).ConfigureAwait(false);
        if (admission is null || admission.SourceTenantScopeKey != scope.TenantScopeKey || admission.BindingHash != prepared.BindingHash ||
            !WorkflowCollaborationAdmissionProof.TryRead(admission, out var proof)) return null;
        var control = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationControlRecord>(WorkflowCollaborationControlSql.Find, parameters, token).ConfigureAwait(false);
        if (control is not { StatusKey: "received", EnvelopeMessageId: not null, ReceivedAtUtc: not null } ||
            !WorkflowCollaborationControlPolicy.TryRead(control, admission, out var body)) return null;
        var call = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationCallRecord>(WorkflowCollaborationCallSql.Find, parameters, token).ConfigureAwait(false);
        var root = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            P(("Id", proof!.SourceInstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (call is null || root is null || call.InstanceId != root.Id || call.BindingHash != admission.BindingHash ||
            call.ExecutionEpochId != proof.ExecutionEpochId || call.InputFormRevision != proof.InputFormRevision || call.InputHash != proof.InputHash ||
            WorkflowCollaborationCallPolicy.Hash(call.InputJson) != proof.InputHash || call.ProjectionHash != proof.ProjectionHash ||
            root.DefinitionVersionId != proof.SourceDefinitionVersionId || root.FormVersionId != proof.SourceFormVersionId || root.TenantId != proof.SourceTenantId) return null;
        return new(admission, proof, root, call, control, body!);
    }

    private async Task<SourceState?> LockAsync(WorkflowCollaborationAdmissionRecord admission, string kind, string status, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (!WorkflowCollaborationAdmissionProof.TryRead(admission, out var proof) ||
            await commands.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                P(("Id", proof!.SourceInstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 1) return null;
        var state = await ReadAsync(admission, kind, token).ConfigureAwait(false);
        if (state is null || state.Admission.StatusKey != status) return null;
        var parameters = Params(state, ("AdmissionStatus", status));
        WorkflowCollaborationAcceptancePolicy? accepted = null;
        if (kind != "cancelled")
        {
            if (state.Root.StatusKey != "active" || await commands.ExecuteAsync(WorkflowCollaborationControlSql.LockSourceGrant, parameters, token).ConfigureAwait(false) != 1) return null;
            var grant = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find,
                P(("Id", proof.GrantId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (grant is null || grant.PolicyHash != proof.PolicyHash || !WorkflowCollaborationGrantPolicy.TryAuthorize(grant,
                    scope.TenantScopeKey, false, proof.GrantRevision, clock.UtcNow, out _, out accepted)) return null;
        }
        if (await commands.ExecuteAsync(WorkflowCollaborationControlSql.LockSource, parameters, token).ConfigureAwait(false) != 1) return null;
        var step = await query.QuerySingleOrDefaultAsync<long?>(WorkflowCollaborationControlSql.ActiveSourceStep, parameters, token).ConfigureAwait(false);
        var active = step is not null && (kind == "cancelled"
            ? await WorkflowExecutionFrameScope.IsRetainedAsync(query, state.Root, proof.ExecutionEpochId, proof.ParallelJoinId, proof.ParallelBranchKey, token).ConfigureAwait(false)
            : await WorkflowExecutionFrameScope.IsValidAsync(query, state.Root, proof.ExecutionEpochId, proof.ParallelJoinId, proof.ParallelBranchKey, token).ConfigureAwait(false));
        if (kind != "cancelled" && !active) return null;
        return state with { StepRevision = active ? step : null, Accepted = accepted };
    }

    private Dictionary<string, object?> Params(SourceState state, params (string Name, object? Value)[] values) => P(new[]
    {
        ("Id", (object?)state.Call.Id), ("InstanceId", (object?)state.Root.Id), ("TenantScopeKey", (object?)state.Call.TenantScopeKey),
        ("TargetTenantScopeKey", (object?)state.Binding.TargetTenantScopeKey), ("TargetInstanceId", (object?)state.Body.TargetInstanceId),
        ("BindingHash", (object?)state.Call.BindingHash), ("GrantId", (object?)state.Binding.GrantId),
        ("GrantRevision", (object?)state.Binding.GrantRevision), ("PolicyHash", (object?)state.Binding.PolicyHash),
        ("ControlId", (object?)state.Control.Id), ("KindKey", (object?)state.Control.KindKey), ("BodyHash", (object?)state.Control.BodyHash),
        ("Revision", (object?)state.Control.Revision), ("CallRevision", (object?)state.Call.Revision),
        ("UnusedActivities", (object?)state.Body.UnusedActivities), ("Now", (object?)clock.UtcNow)
    }.Concat(values).ToArray());
    private static Dictionary<string, object?> P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<bool> Invalid() => Result<bool>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
    private sealed record SourceState(WorkflowCollaborationAdmissionRecord Admission, WorkflowCollaborationCallBinding Binding, WorkflowInstanceRecord Root,
        WorkflowCollaborationCallRecord Call, WorkflowCollaborationControlRecord Control, WorkflowCollaborationControlBody Body,
        long? StepRevision = null, WorkflowCollaborationAcceptancePolicy? Accepted = null);
}
