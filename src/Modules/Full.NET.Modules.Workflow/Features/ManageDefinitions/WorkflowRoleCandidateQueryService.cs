using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

/// <summary>按可信 Host/Tenant 作用域选择 Identity 角色候选目录。</summary>
/// <param name="currentTenant">由认证与租户中间件建立的可信当前租户。</param>
/// <param name="roleMemberDirectory">活动角色批量目录。</param>
internal sealed class WorkflowRoleCandidateQueryService(
    ICurrentTenant currentTenant,
    IWorkflowRoleMemberDirectory roleMemberDirectory)
{
    /// <summary>分页读取与当前工作流管理作用域一致的角色候选。</summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">受控单页数量。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>保持 Workflow HTTP 契约稳定的候选分页结果。</returns>
    public Task<WorkflowRoleCandidatePageResponse> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        _ = WorkflowManagementScope.Resolve(currentTenant);
        return ListCoreAsync(page, pageSize, cancellationToken);
    }

    /// <summary>委托 Identity Contract 读取角色候选分页。</summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">受控单页数量。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>角色候选分页结果。</returns>
    private async Task<WorkflowRoleCandidatePageResponse> ListCoreAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var result = await roleMemberDirectory
            .ListActiveRolesAsync(page, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return new WorkflowRoleCandidatePageResponse(
            result.Items.Select(item => new WorkflowRoleCandidateResponse(
                item.Id,
                item.Code,
                item.Name)).ToArray(),
            result.Page,
            result.PageSize,
            result.Total);
    }
}
