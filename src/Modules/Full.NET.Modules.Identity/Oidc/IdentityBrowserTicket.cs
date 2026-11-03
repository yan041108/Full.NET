namespace Full.NET.Modules.Identity.Oidc;

/// <summary>仅服务端可解密的 BFF 票据；不作为 HTTP 响应或日志载荷。</summary>
internal sealed record IdentityBrowserTicket(string AccessToken, string RefreshToken, string IdentityToken,
    string CsrfToken, DateTimeOffset AccessExpiresAtUtc);

/// <summary>浏览器可见会话状态，故意不包含 OAuth 令牌。</summary>
internal sealed record IdentityBrowserSessionResponse(DateTimeOffset ExpiresAtUtc, string CsrfToken);
