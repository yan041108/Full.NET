using Full.NET.Modules.Identity.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Http;

internal sealed class IdentityCookieWriter(IOptions<IdentityOptions> options)
{
    public const string ProductionRefreshCookieName = "__Host-fullnet-refresh";
    public const string DevelopmentRefreshCookieName = "fullnet-refresh";
    public const string CsrfCookieName = "fullnet-csrf";

    private readonly IdentityOptions _options = options.Value;

    /// <summary>
    /// 生产使用 __Host- 前缀；本地 HTTP 开发改用无前缀名称，否则浏览器会拒绝写入 Secure Cookie。
    /// </summary>
    public string RefreshCookieName =>
        _options.RequireSecureCookies
            ? ProductionRefreshCookieName
            : DevelopmentRefreshCookieName;

    public void Write(
        HttpResponse response,
        string refreshToken,
        string csrfToken)
    {
        var maxAge = TimeSpan.FromDays(_options.RefreshTokenDays);
        response.Cookies.Append(
            RefreshCookieName,
            refreshToken,
            CreateCookieOptions(maxAge, httpOnly: true));
        response.Cookies.Append(
            CsrfCookieName,
            csrfToken,
            CreateCookieOptions(maxAge, httpOnly: false));
    }

    public void Delete(HttpResponse response)
    {
        response.Cookies.Delete(
            RefreshCookieName,
            CreateCookieOptions(TimeSpan.Zero, httpOnly: true));
        response.Cookies.Delete(
            CsrfCookieName,
            CreateCookieOptions(TimeSpan.Zero, httpOnly: false));
    }

    private CookieOptions CreateCookieOptions(TimeSpan maxAge, bool httpOnly) => new()
    {
        HttpOnly = httpOnly,
        Secure = _options.RequireSecureCookies,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = maxAge,
        IsEssential = true,
    };
}
