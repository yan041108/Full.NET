using Full.NET.AI.Abstractions.Credentials;
using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.AI.Providers.Http;

/// <summary>Provider 所有的配置凭据保护实现；保持历史 purpose，避免旧配置失效。</summary>
/// <param name="protection">宿主持久化密钥环对应的保护服务。</param>
public sealed class AiModelCredentialProtector(IDataProtectionProvider protection) : IAiModelCredentialProtector
{
    /// <inheritdoc/>
    public string Protect(string credential) =>
        protection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Protect(credential);
}
