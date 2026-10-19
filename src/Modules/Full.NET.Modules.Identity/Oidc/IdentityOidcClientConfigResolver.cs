// OIDC 客户端配置解析；合并数据库与配置节，拒绝未登记客户端。
using Full.NET.Modules.Identity.Configuration;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>OIDC 客户端配置解析；合并数据库与配置节，拒绝未登记客户端。</summary>
internal sealed class IdentityOidcClientConfigResolver(
    IOptions<IdentityOidcOptions> oidcOptions,
    IOpenIddictApplicationManager applicationManager)
{
    private readonly IdentityOidcOptions _oidcOptions = oidcOptions.Value;

    public IdentityOidcClientConfig? TryResolveFromOptions(string clientId)
    {
        var client = _oidcOptions.Clients.FirstOrDefault(
            item => string.Equals(item.ClientId, clientId, StringComparison.Ordinal));
        if (client is null)
        {
            return null;
        }

        return new IdentityOidcClientConfig(
            client.IsFirstParty,
            string.IsNullOrWhiteSpace(client.ResourceAudience) ? null : client.ResourceAudience,
            false);
    }

    public async Task<IdentityOidcClientConfig?> ResolveAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        var fromOptions = TryResolveFromOptions(clientId);
        if (fromOptions is not null)
        {
            return fromOptions;
        }

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
