using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

/// <summary>岗位选择只通过 Organization 权威目录，Host 作用域不扩大到租户组织。</summary>
internal sealed class WorkflowPositionCandidateQueryService(ICurrentTenant currentTenant, IWorkflowOrganizationAssigneeDirectory directory)
{
    public async Task<WorkflowPositionCandidatePageResponse> ListAsync(int page, int pageSize, CancellationToken token = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (!scope.TenantId.HasValue) return new([], page, pageSize, 0);
        var result = await directory.ListActivePositionsAsync(page, pageSize, token).ConfigureAwait(false);
        return new(result.Items.Select(item => new WorkflowPositionCandidateResponse(item.Id, item.Code, item.Name)).ToArray(),
            result.Page, result.PageSize, result.Total);
    }
}
