using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>将 OIDC 签名环状态同步到 OpenIddict Server 选项，供启动配置与运行时激活复用。</summary>
internal static class IdentityOidcSigningCredentialSync
{
    public static void Apply(OpenIddictServerOptions options, IdentityOidcSigningKeyRing keyRing)
    {
        options.SigningCredentials.Clear();
        options.SigningCredentials.Add(keyRing.SigningCredentials);
        foreach (var pair in keyRing.Entries)
        {
            if (string.Equals(pair.Key, keyRing.ActiveSigningKeyId, StringComparison.Ordinal))
            {
                continue;
            }

            // 仅将无私钥的退役公钥暴露到 JWKS；多把私钥同时注册时 OpenIddict 可能选错活跃 kid。
            if (!pair.Value.HasPrivateKey)
            {
                options.SigningCredentials.Add(
                    new SigningCredentials(pair.Value.SecurityKey, SecurityAlgorithms.RsaSha256));
            }
        }
    }
}