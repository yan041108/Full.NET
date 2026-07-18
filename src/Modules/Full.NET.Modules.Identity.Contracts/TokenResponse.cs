namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 返回短期访问令牌；刷新令牌只通过安全 Cookie 传输。
/// </summary>
public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc);
