using Full.NET.Abstractions.Tenancy;

namespace Full.NET.Modules.Identity.Features.AcceptTenantInvitation;

/// <summary>在单次请求内临时绑定邀请目标租户上下文，供跨租户邀请接受流程写入成员数据。</summary>
internal static class IdentityTenantInvitationScope
{
    internal static async Task<T> RunAsync<T>(
        ICurrentTenantContextWriter currentTenant,
        TenantContext tenant,
        Func<Task<T>> action)
    {
        var wasHost = currentTenant.IsHost;
        var previousTenantId = currentTenant.Id;
        var previousIdentifier = currentTenant.Identifier;
        var previousName = currentTenant.Name;

        currentTenant.SetTenant(tenant);
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
