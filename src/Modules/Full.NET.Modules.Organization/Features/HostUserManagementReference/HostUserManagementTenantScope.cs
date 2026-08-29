using Full.NET.Abstractions.Tenancy;

namespace Full.NET.Modules.Organization.Features.HostUserManagementReference;

/// <summary>在单次请求内临时绑定租户上下文，供 Host 用户管理页跨租户读写机构数据。</summary>
internal static class HostUserManagementTenantScope
{
    internal static async Task<T> RunAsync<T>(
        ICurrentTenantContextWriter currentTenant,
        Guid tenantId,
        string identifier,
        string name,
        Func<Task<T>> action)
    {
        var wasHost = currentTenant.IsHost;
        var previousTenantId = currentTenant.Id;
        var previousIdentifier = currentTenant.Identifier;
        var previousName = currentTenant.Name;

        currentTenant.SetTenant(new TenantContext(tenantId, identifier, name));
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            if (wasHost)
            {
                currentTenant.SetHost();
            }
            else if (previousTenantId is Guid restoredTenantId
                     && previousIdentifier is not null
                     && previousName is not null)
            {
                currentTenant.SetTenant(
                    new TenantContext(restoredTenantId, previousIdentifier, previousName));
            }
            else
            {
                currentTenant.Clear();
            }
        }
    }
}
