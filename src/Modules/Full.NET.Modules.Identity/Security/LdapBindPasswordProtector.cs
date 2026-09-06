using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Identity.Security;

/// <summary>使用 Data Protection 保护 LDAP 绑定密码，避免明文落库。</summary>
internal sealed class LdapBindPasswordProtector(IDataProtectionProvider dataProtectionProvider)
{
    private const string Purpose = "Full.NET.Identity.LdapBindPassword.v1";

    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector(Purpose);

    /// <summary>保护绑定密码。</summary>
    /// <param name="bindPassword">明文绑定密码。</param>
    /// <returns>受保护密文。</returns>
    public string Protect(string bindPassword) =>
        _protector.Protect(bindPassword);

    /// <summary>解保护绑定密码；仅供内部 LDAP 客户端调用。</summary>
    /// <param name="bindPasswordProtected">受保护密文。</param>
    /// <returns>明文绑定密码。</returns>
    public string Unprotect(string bindPasswordProtected) =>
        _protector.Unprotect(bindPasswordProtected);
}
