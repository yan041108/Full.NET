using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Notifications.Domain;

/// <summary>使用 Data Protection 保护收件端点原值，避免明文落库或进入查询投影。</summary>
internal sealed class NotificationRecipientEndpointProtector(IDataProtectionProvider dataProtectionProvider)
{
    private const string Purpose = "Full.NET.Notifications.RecipientEndpoint.v1";

    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector(Purpose);

    public string Protect(string rawValue) => _protector.Protect(rawValue);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
