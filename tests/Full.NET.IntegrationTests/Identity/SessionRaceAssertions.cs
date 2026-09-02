using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// 验证 Refresh 与租户上下文切换在并发下的会话权威性与稳定错误码。
/// </summary>
internal static class SessionRaceAssertions
{
    public static async Task VerifyAsync(
        Api.FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        // 并发会话测试必须固定使用请求中显式携带的 Cookie，避免首个响应更新 Cookie 容器后把第二次请求变成合法的串行轮换。
        using var client = factory.CreateClientForHost("localhost", handleCookies: false);

        await VerifyConcurrentRefreshAsync(client, cancellationToken);
        await VerifyConcurrentContextSwitchAsync(factory, client, cancellationToken);
        await VerifyRefreshUsesSessionTenantAsync(client, cancellationToken);
        await VerifyConcurrentRefreshAndContextSwitchAsync(client, cancellationToken);
    }

    private static async Task VerifyConcurrentRefreshAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var session = await LoginAsync(client, cancellationToken);
        var firstTask = client.SendAsync(CreateRefreshRequest(session), cancellationToken);
        var secondTask = client.SendAsync(CreateRefreshRequest(session), cancellationToken);
        await Task.WhenAll(firstTask, secondTask);
        using var firstResponse = await firstTask;
        using var secondResponse = await secondTask;

        var statuses = new[] { firstResponse.StatusCode, secondResponse.StatusCode };
        Assert.AreEqual(1, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.AreEqual(1, statuses.Count(status => status == HttpStatusCode.Unauthorized));

        var failure = firstResponse.StatusCode == HttpStatusCode.OK
            ? secondResponse
            : firstResponse;
        using var problem = JsonDocument.Parse(
            await failure.Content.ReadAsStringAsync(cancellationToken));
        var code = problem.RootElement.GetProperty("code").GetString();
        Assert.IsTrue(
            code is IdentityErrorCodes.RefreshTokenReuseDetected
                or IdentityErrorCodes.InvalidRefreshToken,
            $"Unexpected failure code: {code}");
    }

    private static async Task VerifyConcurrentContextSwitchAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var session = await LoginAsync(client, cancellationToken);
        var acme = await GetAcmeTenantAsync(client, session.AccessToken, cancellationToken);
        var principal = CreatePrincipalFromAccessToken(session.AccessToken);
        var tenant = new VerifiedTenantContext(
            acme.Id,
            acme.Identifier,
            acme.Name,
            acme.Domain);

        var results = await Task.WhenAll(
            ChangeTenantAsync(factory, principal, tenant, cancellationToken),
            ChangeTenantAsync(factory, principal, tenant, cancellationToken));

        Assert.AreEqual(1, results.Count(result => result.IsSuccess));
        Assert.AreEqual(1, results.Count(result =>
            result.Error?.Code == IdentityErrorCodes.SessionContextConflict));
    }

    private static async Task<Full.NET.Abstractions.Results.Result<TenantContextTokenResponse>>
        ChangeTenantAsync(
            Api.FullNetApiFactory factory,
            ClaimsPrincipal principal,
            VerifiedTenantContext tenant,
            CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        return await scope.ServiceProvider
            .GetRequiredService<IIdentitySessionContextService>()
            .ChangeAsync(principal, tenant, cancellationToken);
    }

    private static async Task VerifyRefreshUsesSessionTenantAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var session = await LoginAsync(client, cancellationToken);
        var hostToken = session.AccessToken;
        var acme = await GetAcmeTenantAsync(client, hostToken, cancellationToken);

        using var enterRequest = CreateContextRequest(acme.Id, hostToken);
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);

        using var refreshRequest = CreateRefreshRequest(session);
        using var refreshResponse = await client.SendAsync(refreshRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(refreshed);

        using var jwt = JsonDocument.Parse(DecodeJwtPayload(refreshed.AccessToken));
        Assert.AreEqual(
            acme.Id.ToString("D"),
            jwt.RootElement.GetProperty("fullnet_tenant_id").GetString());
    }

    private static async Task VerifyConcurrentRefreshAndContextSwitchAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var session = await LoginAsync(client, cancellationToken);
        var acme = await GetAcmeTenantAsync(client, session.AccessToken, cancellationToken);

        var refreshTask = client.SendAsync(CreateRefreshRequest(session), cancellationToken);
        var contextTask = client.SendAsync(
            CreateContextRequest(acme.Id, session.AccessToken),
            cancellationToken);
        await Task.WhenAll(refreshTask, contextTask);
        using var refreshResponse = await refreshTask;
        using var contextResponse = await contextTask;

        Assert.AreNotEqual(HttpStatusCode.InternalServerError, refreshResponse.StatusCode);
        Assert.AreNotEqual(HttpStatusCode.InternalServerError, contextResponse.StatusCode);
        Assert.IsTrue(
            refreshResponse.StatusCode == HttpStatusCode.OK
            || contextResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);

        var recovered = await LoginAsync(client, cancellationToken);
        using var enterRequest = CreateContextRequest(acme.Id, recovered.AccessToken);
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
    }

    private static async Task<AuthenticatedSession> LoginAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                username = "admin",
                password = Api.FullNetApiFactory.TestPassword,
            }),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(token);
        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToArray();
        var refreshCookie = ExtractCookie(cookies, "__Host-fullnet-refresh");
        var csrfCookie = ExtractCookie(cookies, "fullnet-csrf");
        return new AuthenticatedSession(
            token.AccessToken,
            refreshCookie,
            csrfCookie,
            $"__Host-fullnet-refresh={refreshCookie}; fullnet-csrf={csrfCookie}");
    }

    private static async Task<TenantContextSummary> GetAcmeTenantAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerRequest(
            HttpMethod.Get,
            "/api/v1/tenancy/available",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var available = await response.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        return available.Single(tenant => tenant.Identifier == "acme");
    }

    private static HttpRequestMessage CreateRefreshRequest(AuthenticatedSession session)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add("Cookie", session.CookieHeader);
        request.Headers.Add("X-CSRF-Token", session.CsrfCookie);
        return request;
    }

    private static HttpRequestMessage CreateBearerRequest(
        HttpMethod method,
        string path,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }

    private static HttpRequestMessage CreateContextRequest(
        Guid? tenantId,
        string accessToken)
    {
        var request = CreateBearerRequest(
            HttpMethod.Put,
            "/api/v1/tenancy/context",
            accessToken);
        request.Content = JsonContent.Create(new ChangeTenantContextRequest(tenantId));
        return request;
    }

    private static string ExtractCookie(
        IEnumerable<string> cookies,
        string name)
    {
        var cookie = cookies.Single(value => value.StartsWith(
            $"{name}=",
            StringComparison.Ordinal));
        return cookie.Split(';', 2)[0][(name.Length + 1)..];
    }

    private static ClaimsPrincipal CreatePrincipalFromAccessToken(string accessToken)
    {
        var jwt = new JsonWebToken(accessToken);
        var identity = new ClaimsIdentity("Bearer");
        foreach (var claim in jwt.Claims)
        {
            identity.AddClaim(new Claim(claim.Type, claim.Value));
        }

        return new ClaimsPrincipal(identity);
    }

    private static string DecodeJwtPayload(string accessToken)
    {
        var encoded = accessToken.Split('.')[1]
            .Replace('-', '+')
            .Replace('_', '/');
        encoded = encoded.PadRight(
            encoded.Length + ((4 - encoded.Length % 4) % 4),
            '=');
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
    }

    private sealed record AuthenticatedSession(
        string AccessToken,
        string RefreshCookie,
        string CsrfCookie,
        string CookieHeader);
}
