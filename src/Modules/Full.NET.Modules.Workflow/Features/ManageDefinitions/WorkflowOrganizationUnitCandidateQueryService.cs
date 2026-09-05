using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

/// <summary>按可信 Tenant 作用域选择 Organization 机构单元候选目录。</summary>
/// <param name="currentTenant">由认证与租户中间件建立的可信当前租户。</param>
/// <param name="unitLeaderDirectory">活动机构单元批量目录。</param>
internal sealed class WorkflowOrganizationUnitCandidateQueryService(
    ICurrentTenant currentTenant,
    IWorkflowUnitLeaderDirectory unitLeaderDirectory)
{
    /// <summary>分页读取当前 Tenant 可配置为办理人来源的机构单元候选。</summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">受控单页数量。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>机构单元候选分页结果；Host 作用域返回空页。</returns>
    public async Task<WorkflowOrganizationUnitCandidatePageResponse> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (!scope.TenantId.HasValue)
        {
            return new WorkflowOrganizationUnitCandidatePageResponse([], page, pageSize, 0);
        }

        var result = await unitLeaderDirectory
            .ListActiveUnitsAsync(page, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return new WorkflowOrganizationUnitCandidatePageResponse(
            result.Items.Select(item => new WorkflowOrganizationUnitCandidateResponse(
                item.Id,
                item.Code,
                item.Name)).ToArray(),
            result.Page,
            result.PageSize,
            result.Total);
    }
}
