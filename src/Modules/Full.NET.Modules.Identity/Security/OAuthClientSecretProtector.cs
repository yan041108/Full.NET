using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Identity.Security;

/// <summary>使用 Data Protection 保护 OAuth 客户端密钥，避免明文落库。</summary>
internal sealed class OAuthClientSecretProtector(IDataProtectionProvider dataProtectionProvider)
{
    private const string Purpose = "Full.NET.Identity.OAuthClientSecret.v1";

    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector(Purpose);

    /// <summary>保护客户端密钥。</summary>
    /// <param name="clientSecret">明文客户端密钥。</param>
    /// <returns>受保护密文。</returns>
    public string Protect(string clientSecret) =>
        _protector.Protect(clientSecret);

    /// <summary>解保护客户端密钥；仅供内部 OIDC 客户端调用。</summary>
    /// <param name="clientSecretProtected">受保护密文。</param>
    /// <returns>明文客户端密钥。</returns>
    public string Unprotect(string clientSecretProtected) =>
        _protector.Unprotect(clientSecretProtected);
}
