// Webhook 签名密钥保护；落库前加密，日志与 API 不返回明文。
using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Webhooks.Security;

/// <summary>Webhook 签名密钥保护；落库前加密，日志与 API 不返回明文。</summary>
internal sealed class WebhookSigningSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Full.NET.Webhooks.SigningSecret");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
