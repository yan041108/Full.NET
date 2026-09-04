using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

/// <summary>按可信 Host/Tenant 作用域选择 Identity 用户候选目录。</summary>
/// <param name="currentTenant">由认证与租户中间件建立的可信当前租户。</param>
/// <param name="hostUsers">活动 Host 用户候选目录。</param>
/// <param name="tenantUsers">当前 Tenant 活动用户候选目录。</param>
internal sealed class WorkflowRecipientCandidateQueryService(
    ICurrentTenant currentTenant,
    IHostUserSelectionDirectory hostUsers,
    ITenantUserSelectionDirectory tenantUsers)
{
    /// <summary>分页读取与当前工作流管理作用域一致的收件人候选。</summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">受控单页数量。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>保持 Workflow HTTP 契约稳定的候选分页结果。</returns>
    public async Task<WorkflowRecipientCandidatePageResponse> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (scope.TenantId.HasValue)
        {
            // Tenant 请求只委托给当前租户目录，避免设计器枚举整个 Host 用户空间。
            var result = await tenantUsers.ListActiveTenantUsersAsync(
                    page,
                    pageSize,
                    cancellationToken)
                .ConfigureAwait(false);
            return new WorkflowRecipientCandidatePageResponse(
                result.Items.Select(item => new WorkflowRecipientCandidateResponse(
                    item.Id,
                    item.Username,
                    item.DisplayName)).ToArray(),
                result.Page,
                result.PageSize,
                result.Total);
        }

        var hostResult = await hostUsers.ListActiveHostUsersAsync(
                page,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);
        return new WorkflowRecipientCandidatePageResponse(
            hostResult.Items.Select(item => new WorkflowRecipientCandidateResponse(
                item.Id,
                item.Username,
                item.DisplayName)).ToArray(),
            hostResult.Page,
            hostResult.PageSize,
            hostResult.Total);
    }
}
