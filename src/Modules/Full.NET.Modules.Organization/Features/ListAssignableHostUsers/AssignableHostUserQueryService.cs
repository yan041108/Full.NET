using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Organization.Features.ListAssignableHostUsers;

/// <summary>
/// 为组织关系写入表单提供活动 Host 用户候选，且只允许在明确租户上下文中读取。
/// </summary>
internal sealed class AssignableHostUserQueryService(
    IHostUserSelectionDirectory hostUserDirectory,
    ICurrentTenant currentTenant)
{
    public async Task<PagedResult<OrganizationAssignableUserResponse>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var users = await hostUserDirectory.ListActiveHostUsersAsync(
                page,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);
        var items = users.Items
            .Select(user => new OrganizationAssignableUserResponse(
                user.Id,
                user.Username,
                user.DisplayName))
            .ToArray();

        return new PagedResult<OrganizationAssignableUserResponse>(
            items,
            users.Page,
            users.PageSize,
            users.Total);
    }

    private void EnsureTenantContext()
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
        {
            throw new TenantContextMissingException(
                "organization.tenant_context_required");
        }
    }
}
