using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试登录辅助：API 创建用户登录后自动完成首次改密。</summary>
internal static class IntegrationTestAuthHelper
{
    public const string ClearedPassword = "FullNet!2026Cleared";

    public static async Task<string> LoginAsHostUserAsync(
        HttpClient client,
        string username,
        string password = FullNetApiFactory.TestPassword,
        CancellationToken cancellationToken = default)
    {
        var session = await TryLoginAsync(client, username, password, cancellationToken)
            ?? await TryLoginAsync(client, username, ClearedPassword, cancellationToken);
        Assert.IsNotNull(session, $"Host user '{username}' login failed.");

        if (!await RequiresPasswordChangeAsync(client, session, cancellationToken))
        {
            return session.AccessToken;
        }

        return await ChangePasswordAsync(
            client,
            session,
            password,
            ClearedPassword,
            cancellationToken);
    }

    private static async Task<AuthenticatedSession?> TryLoginAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        if (loginResponse.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        Assert.IsNotNull(token);
        if (!loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            throw new InvalidOperationException("Login response did not include session cookies.");
        }

        return CreateSession(token.AccessToken, cookies);
    }

    private static async Task<bool> RequiresPasswordChangeAsync(
        HttpClient client,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);
        Assert.IsNotNull(profile);
        return profile.PasswordChangeRequired;
    }

    private static async Task<string> ChangePasswordAsync(
        HttpClient client,
        AuthenticatedSession session,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/me/password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(currentPassword, newPassword)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add("Cookie", session.CookieHeader);
        request.Headers.Add("X-CSRF-Token", session.CsrfCookie);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            await response.Content.ReadAsStringAsync(cancellationToken));
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }

    private static AuthenticatedSession CreateSession(
        string accessToken,
        IEnumerable<string> cookies)
    {
        var refreshCookie = ExtractCookie(cookies, RefreshCookieNames.Production)
            ?? ExtractCookie(cookies, RefreshCookieNames.Development);
        Assert.IsNotNull(refreshCookie, "Login response did not include refresh cookie.");
        var csrfCookie = ExtractCookie(cookies, CsrfCookieName);
        Assert.IsNotNull(csrfCookie, "Login response did not include CSRF cookie.");
        var refreshName = ExtractCookie(cookies, RefreshCookieNames.Production) is not null
            ? RefreshCookieNames.Production
            : RefreshCookieNames.Development;
        return new AuthenticatedSession(
            accessToken,
            csrfCookie,
            $"{refreshName}={refreshCookie}; {CsrfCookieName}={csrfCookie}");
    }

    private static string? ExtractCookie(IEnumerable<string> cookies, string name)
    {
        var cookie = cookies.FirstOrDefault(value => value.StartsWith(
            $"{name}=",
            StringComparison.Ordinal));
        return cookie is null
            ? null
            : cookie.Split(';', 2)[0][(name.Length + 1)..];
    }

    private sealed record AuthenticatedSession(
        string AccessToken,
        string CsrfCookie,
        string CookieHeader);

    private const string CsrfCookieName = "fullnet-csrf";

    private static class RefreshCookieNames
    {
        public const string Production = "__Host-fullnet-refresh";
        public const string Development = "fullnet-refresh";
    }
}