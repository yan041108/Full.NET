// OIDC 服务器选项配置；端点、令牌格式与签名密钥绑定。
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>OIDC 服务器选项配置；端点、令牌格式与签名密钥绑定。</summary>
internal sealed class IdentityOidcServerOptionsConfigurer(IdentityOidcSigningKeyRing keyRing)
    : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options) =>
        IdentityOidcSigningCredentialSync.Apply(options, keyRing);
}
