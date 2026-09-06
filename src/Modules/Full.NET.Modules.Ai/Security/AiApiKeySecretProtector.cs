using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Ai.Security;

/// <summary>使用 Data Protection 保护 AI API 密钥，避免明文落库。</summary>
internal sealed class AiApiKeySecretProtector(IDataProtectionProvider dataProtectionProvider)
{
    private const string Purpose = "Full.NET.Ai.ModelConfigApiKey.v1";

    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector(Purpose);

    /// <summary>保护 API 密钥。</summary>
    /// <param name="apiKey">明文密钥。</param>
    /// <returns>受保护密文。</returns>
    public string Protect(string apiKey) =>
        _protector.Protect(apiKey);

    /// <summary>解保护 API 密钥；仅供内部连通性测试与后续推理调用使用。</summary>
    /// <param name="apiKeyProtected">受保护密文。</param>
    /// <returns>明文密钥。</returns>
    public string Unprotect(string apiKeyProtected) =>
        _protector.Unprotect(apiKeyProtected);
}
