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
        foreach (var key in keyRing.ValidationKeys)
        {
            if (string.Equals(key.KeyId, keyRing.ActiveSigningKeyId, StringComparison.Ordinal))
            {
                continue;
            }

            // 候选密钥必须提前发布公钥；不把候选私钥注册为签发凭据，避免组件选错活动 kid。
            var rsaKey = (RsaSecurityKey)key;
            var publicKey = new RsaSecurityKey(rsaKey.Rsa.ExportParameters(false)) { KeyId = key.KeyId };
            options.SigningCredentials.Add(new SigningCredentials(publicKey, SecurityAlgorithms.RsaSha256));
        }
    }
}
