using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Features.ManageInstances;

internal sealed partial class WorkflowInstanceManagementService
{
    /// <summary>只依据共享关闭意图取消自己的真实目标根；撤销后不借用人工身份，也不把未决外部效果当作完成。</summary>
    internal async Task<Result<WorkflowInstanceResponse>> CancelFromCollaborationAsync(WorkflowCollaborationAdmissionRecord admission, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (admission.TargetInstanceId is not { } instanceId || admission.TargetTenantScopeKey != scope.TenantScopeKey ||
            !WorkflowCollaborationAdmissionProof.TryRead(admission, out var proof)) return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        return await transaction.ExecuteResultAsync(async inner =>
        {
            if (await commandExecutor.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                    Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false) != 1 ||
                await commandExecutor.ExecuteAsync(WorkflowCollaborationControlSql.LockClosing,
                    Parameters(("Id", admission.Id), ("TenantScopeKey", scope.TenantScopeKey), ("BindingHash", admission.BindingHash),
                        ("TargetInstanceId", instanceId)), inner).ConfigureAwait(false) != 1) return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
            var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
                Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false);
            var origin = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCollaborationOriginRecord>(WorkflowCollaborationRelaySql.FindOrigin,
                Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false);
            if (instance?.FormVersionId is not { } form || origin?.CallId != admission.Id || origin.RootInstanceId != instanceId ||
                instance.DefinitionVersionId != proof!.TargetDefinitionVersionId || instance.BusinessType != WorkflowCollaborationStartContext.BusinessType ||
                instance.BusinessId != proof.CallId.ToString("D")) return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
            if (instance.StatusKey is not ("active" or "suspended")) return Result<WorkflowInstanceResponse>.Success(Map(instance, form, null));
            var request = new CancelWorkflowInstanceRequest(instance.Revision, "workflow.collaboration.closed", "workflow-collaboration-cancel-" + proof.CallId.ToString("N"));
            return await CancelCoreAsync(instanceId, instance.StartedById, request, scope, HashLifecycleRequest(request.ExpectedRevision, request.Reason), inner,
                collaborationContext: new(proof.CallId, admission.BindingHash, instanceId)).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }
}
