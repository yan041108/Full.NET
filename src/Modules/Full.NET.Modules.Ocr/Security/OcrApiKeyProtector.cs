using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Ocr.Security;

/// <summary>使用 Data Protection 保护 OCR Provider API Key。</summary>
internal sealed class OcrApiKeyProtector(IDataProtectionProvider dataProtectionProvider)
{
    private const string Purpose = "Full.NET.Ocr.ProviderApiKey.v1";

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(Purpose);

    public string Protect(string apiKey) => _protector.Protect(apiKey);

    public string Unprotect(string apiKeyProtected) => _protector.Unprotect(apiKeyProtected);
}
