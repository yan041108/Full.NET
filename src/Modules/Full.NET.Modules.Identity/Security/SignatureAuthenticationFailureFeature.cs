using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Security;

/// <summary>签名认证失败时由授权中间件读取并映射为 ProblemDetails。</summary>
internal sealed class SignatureAuthenticationFailureFeature
{
    public required Error Error { get; init; }
}
