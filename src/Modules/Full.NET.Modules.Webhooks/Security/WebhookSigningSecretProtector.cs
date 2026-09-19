using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Webhooks.Security;

internal sealed class WebhookSigningSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Full.NET.Webhooks.SigningSecret");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
