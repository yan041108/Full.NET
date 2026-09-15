using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Full.NET.Modules.Identity.Oidc;

internal sealed class IdentityOidcServerOptionsConfigurer(IdentityOidcSigningKeyRing keyRing)
    : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        // 先注册当前活跃私钥，再追加退役公钥，避免 OpenIddict 在轮换窗口内仍选用旧 kid 签发。
        options.SigningCredentials.Add(keyRing.SigningCredentials);
        foreach (var key in keyRing.ValidationKeys)
        {
            if (key.KeyId is null
                || string.Equals(
                    key.KeyId,
                    keyRing.SigningCredentials.Key.KeyId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            options.SigningCredentials.Add(
                new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        }
    }
}
