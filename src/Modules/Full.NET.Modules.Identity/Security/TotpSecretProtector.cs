using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Identity.Security;

/// <summary>使用 Data Protection 保护 TOTP 共享密钥，避免明文落库。</summary>
internal sealed class TotpSecretProtector(IDataProtectionProvider dataProtectionProvider)
{
    private const string Purpose = "Full.NET.Identity.TotpSecret.v1";

    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector(Purpose);

    public string Protect(string sharedSecretBase32) =>
        _protector.Protect(sharedSecretBase32);

    public string Unprotect(string secretProtected) =>
        _protector.Unprotect(secretProtected);
}
