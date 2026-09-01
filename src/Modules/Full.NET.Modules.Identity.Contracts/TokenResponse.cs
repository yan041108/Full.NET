namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 返回短期访问令牌；刷新令牌只通过安全 Cookie 传输。
/// </summary>
/// <param name="AccessToken">供当前客户端后续调用受保护 API 的短期 Bearer 令牌。</param>
/// <param name="TokenType">令牌类型，当前固定为 Bearer，保留字段用于兼容标准 OAuth 风格响应。</param>
/// <param name="ExpiresAtUtc">访问令牌的 UTC 失效时间点，客户端应以此安排静默刷新或强制登出。</param>
public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc);
