using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.K3Cloud.Security;

/// <summary>使用 Data Protection 保护 K3Cloud 密码，避免明文落库。</summary>
internal sealed class K3CloudPasswordProtector(IDataProtectionProvider dataProtectionProvider)
{
    private const string Purpose = "Full.NET.K3Cloud.ConnectionPassword.v1";

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(Purpose);

    /// <summary>保护密码。</summary>
    public string Protect(string password) => _protector.Protect(password);

    /// <summary>解保护密码；仅供受控远程调用使用。</summary>
    public string Unprotect(string passwordProtected) => _protector.Unprotect(passwordProtected);
}
