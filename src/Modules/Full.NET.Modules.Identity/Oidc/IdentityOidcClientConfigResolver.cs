// OIDC 客户端策略以数据库为唯一权威源，不回退启动配置。
using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>读取数据库中的客户端信任与停用策略；配置仅供 Migrator 首次初始化。</summary>
internal sealed class IdentityOidcClientConfigResolver(
    IOpenIddictApplicationManager applicationManager)
{
    public async Task<IdentityOidcClientConfig?> ResolveAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken)
            .ConfigureAwait(false);
        if (application is null)
        {
            return null;
        }

        var properties = await applicationManager.GetPropertiesAsync(application, cancellationToken)
            .ConfigureAwait(false);
        return IdentityOidcClientMetadata.Read(properties);
    }

    public async Task<bool> IsDisabledAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        var config = await ResolveAsync(clientId, cancellationToken).ConfigureAwait(false);
        return config?.IsDisabled == true;
    }
}
