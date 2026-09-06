using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Reporting.Security;

/// <summary>使用 Data Protection 保护报表数据源密码，避免明文落库。</summary>
internal sealed class ReportingDataSourceSecretProtector(IDataProtectionProvider dataProtectionProvider)
{
    private const string Purpose = "Full.NET.Reporting.DataSourcePassword.v1";

    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector(Purpose);

    /// <summary>保护数据源密码。</summary>
    /// <param name="password">明文密码。</param>
    /// <returns>受保护密文。</returns>
    public string Protect(string password) =>
        _protector.Protect(password);

    /// <summary>解保护数据源密码；仅供内部连接测试与后续查询执行使用。</summary>
    /// <param name="passwordProtected">受保护密文。</param>
    /// <returns>明文密码。</returns>
    public string Unprotect(string passwordProtected) =>
        _protector.Unprotect(passwordProtected);
}
