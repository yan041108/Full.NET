using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.IntegrationTests.Api;

internal static class IdentityApiAssertions
{
    public static async Task VerifyLoginAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        var authorization = await factory.GetHostAuthorizationStateAsync(cancellationToken);
        Assert.AreEqual(1L, authorization.RoleCount);
        Assert.AreEqual(4L, authorization.PermissionCount);
        Assert.AreEqual(1L, authorization.AssignmentCount);
        using var client = factory.CreateClientForHost("localhost");

        using (var preflightRequest = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/v1/auth/login"))
        {
            preflightRequest.Headers.Add("Origin", "http://localhost");
            preflightRequest.Headers.Add("Access-Control-Request-Method", "POST");
            preflightRequest.Headers.Add(
                "Access-Control-Request-Headers",
                "content-type,x-csrf-token");
            using var preflightResponse = await client.SendAsync(
                preflightRequest,
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.NoContent, preflightResponse.StatusCode);
            Assert.AreEqual(
                "http://localhost",
                preflightResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.AreEqual(
                "true",
                preflightResponse.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        }
        using (var rejectedPreflight = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/v1/auth/login"))
        {
            rejectedPreflight.Headers.Add("Origin", "https://untrusted.example");
            rejectedPreflight.Headers.Add("Access-Control-Request-Method", "POST");
            using var rejectedResponse = await client.SendAsync(
                rejectedPreflight,
                cancellationToken);
            Assert.IsFalse(rejectedResponse.Headers.TryGetValues(
                "Access-Control-Allow-Origin",
                out _));
        }

        using var invalidRequest = CreateLoginRequest("wrong-password");
        using var invalidResponse = await client.SendAsync(
            invalidRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        using (var problem = JsonDocument.Parse(
            await invalidResponse.Content.ReadAsStringAsync(cancellationToken)))
        {
            Assert.AreEqual(
                "identity.invalid_credentials",
                problem.RootElement.GetProperty("code").GetString());
        }

        using var loginRequest = CreateLoginRequest(FullNetApiFactory.TestPassword);
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var json = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
        var token = JsonSerializer.Deserialize<TokenResponse>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsNotNull(token);
        Assert.AreEqual("Bearer", token.TokenType);
        Assert.IsFalse(string.IsNullOrWhiteSpace(token.AccessToken));
        var jwt = new JsonWebToken(token.AccessToken);
        Assert.AreEqual("host", jwt.GetClaim(IdentityClaimTypes.ActorScope).Value);
        Assert.AreEqual("host", jwt.GetClaim(IdentityClaimTypes.Scope).Value);
        CollectionAssert.AreEqual(
            new[]
            {
                "identity.navigation.read",
                "platform.dashboard.read",
                "tenancy.tenants.read",
                "tenancy.tenants.switch",
            },
            jwt.Claims
                .Where(claim => claim.Type == IdentityClaimTypes.Permission)
                .Select(claim => claim.Value)
                .ToArray());
        using (var tokenDocument = JsonDocument.Parse(json))
        {
            Assert.IsFalse(tokenDocument.RootElement.TryGetProperty("refreshToken", out _));
            Assert.IsFalse(tokenDocument.RootElement.TryGetProperty("csrfToken", out _));
        }

        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToArray();
        AssertCookie(cookies, "__Host-fullnet-refresh", httpOnly: true);
        AssertCookie(cookies, "fullnet-csrf", httpOnly: false);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.AccessToken);
        using var meResponse = await client.SendAsync(meRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, meResponse.StatusCode);
        var currentUser = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>(
            cancellationToken);
        Assert.IsNotNull(currentUser);
        Assert.AreEqual("admin", currentUser.Username);
        Assert.AreEqual("系统管理员", currentUser.DisplayName);
        Assert.AreEqual("host", currentUser.ActorScope);
        Assert.AreEqual("host", currentUser.Scope);
        Assert.IsNull(currentUser.TenantId);
        CollectionAssert.AreEqual(
            new[]
            {
                "identity.navigation.read",
                "platform.dashboard.read",
                "tenancy.tenants.read",
                "tenancy.tenants.switch",
            },
            currentUser.Permissions.ToArray());

        var refreshCookie = ExtractCookie(cookies, "__Host-fullnet-refresh");
        var csrfCookie = ExtractCookie(cookies, "fullnet-csrf");
        using (var invalidCsrfRequest = CreateSessionRequest(
            "/api/v1/auth/refresh",
            refreshCookie,
            csrfCookie,
            "tampered-csrf"))
        using (var invalidCsrfResponse = await client.SendAsync(
            invalidCsrfRequest,
            cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, invalidCsrfResponse.StatusCode);
        }

        var firstRefreshTask = SendRefreshAsync(
            client,
            refreshCookie,
            csrfCookie,
            cancellationToken);
        var secondRefreshTask = SendRefreshAsync(
            client,
            refreshCookie,
            csrfCookie,
            cancellationToken);
        var concurrentResponses = await Task.WhenAll(firstRefreshTask, secondRefreshTask);
        try
        {
            CollectionAssert.AreEquivalent(
                new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized },
                concurrentResponses.Select(response => response.StatusCode).ToArray());
            var successfulRefresh = concurrentResponses.Single(response => response.IsSuccessStatusCode);
            var rejectedRefresh = concurrentResponses.Single(response => !response.IsSuccessStatusCode);
            using (var reuseProblem = JsonDocument.Parse(
                await rejectedRefresh.Content.ReadAsStringAsync(cancellationToken)))
            {
                Assert.AreEqual(
                    "identity.refresh_token_reuse_detected",
                    reuseProblem.RootElement.GetProperty("code").GetString());
            }

            var rotatedCookies = successfulRefresh.Headers.GetValues("Set-Cookie").ToArray();
            var rotatedRefresh = ExtractCookie(rotatedCookies, "__Host-fullnet-refresh");
            var rotatedCsrf = ExtractCookie(rotatedCookies, "fullnet-csrf");
            using var revokedFamilyRequest = CreateSessionRequest(
                "/api/v1/auth/refresh",
                rotatedRefresh,
                rotatedCsrf,
                rotatedCsrf);
            using var revokedFamilyResponse = await client.SendAsync(
                revokedFamilyRequest,
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.Unauthorized, revokedFamilyResponse.StatusCode);
        }
        finally
        {
            foreach (var response in concurrentResponses)
            {
                response.Dispose();
            }
        }

        var concurrentLoginResponses = await Task.WhenAll(
            SendLoginAsync(client, FullNetApiFactory.TestPassword, cancellationToken),
            SendLoginAsync(client, FullNetApiFactory.TestPassword, cancellationToken));
        try
        {
            Assert.IsTrue(concurrentLoginResponses.All(
                response => response.StatusCode == HttpStatusCode.OK));
            var secondLoginCookies = concurrentLoginResponses[0]
                .Headers.GetValues("Set-Cookie")
                .ToArray();
            var logoutRefresh = ExtractCookie(secondLoginCookies, "__Host-fullnet-refresh");
            var logoutCsrf = ExtractCookie(secondLoginCookies, "fullnet-csrf");
            using var logoutRequest = CreateSessionRequest(
                "/api/v1/auth/logout",
                logoutRefresh,
                logoutCsrf,
                logoutCsrf);
            using var logoutResponse = await client.SendAsync(
                logoutRequest,
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.NoContent, logoutResponse.StatusCode);

            using var afterLogoutRequest = CreateSessionRequest(
                "/api/v1/auth/refresh",
                logoutRefresh,
                logoutCsrf,
                logoutCsrf);
            using var afterLogoutResponse = await client.SendAsync(
                afterLogoutRequest,
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.Unauthorized, afterLogoutResponse.StatusCode);
        }
        finally
        {
            foreach (var response in concurrentLoginResponses)
            {
                response.Dispose();
            }
        }

        var failedLoginTasks = Enumerable.Range(0, 5)
            .Select(_ => SendLoginAsync(client, "Wrong!2026Password", cancellationToken))
            .ToArray();
        var failedLoginResponses = await Task.WhenAll(failedLoginTasks);
        try
        {
            Assert.IsTrue(failedLoginResponses.All(
                response => response.StatusCode == HttpStatusCode.Unauthorized));
        }
        finally
        {
            foreach (var response in failedLoginResponses)
            {
                response.Dispose();
            }
        }

        using var lockedLoginRequest = CreateLoginRequest(FullNetApiFactory.TestPassword);
        using var lockedLoginResponse = await client.SendAsync(
            lockedLoginRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, lockedLoginResponse.StatusCode);

        Assert.IsGreaterThanOrEqualTo(
            6L,
            await factory.GetAuthenticationAuditCountAsync(cancellationToken));
    }

    private static HttpRequestMessage CreateLoginRequest(string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                username = "admin",
                password,
            }),
        };
        request.Headers.Add("Origin", "http://localhost");
        return request;
    }

    private static void AssertCookie(
        IEnumerable<string> cookies,
        string name,
        bool httpOnly)
    {
        var cookie = cookies.Single(value => value.StartsWith(
            $"{name}=",
            StringComparison.Ordinal));
        StringAssert.Contains(cookie, "path=/", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(cookie, "secure", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(cookie, "samesite=strict", StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(
            httpOnly,
            cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<HttpResponseMessage> SendRefreshAsync(
        HttpClient client,
        string refreshCookie,
        string csrfCookie,
        CancellationToken cancellationToken)
    {
        using var request = CreateSessionRequest(
            "/api/v1/auth/refresh",
            refreshCookie,
            csrfCookie,
            csrfCookie);
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendLoginAsync(
        HttpClient client,
        string password,
        CancellationToken cancellationToken)
    {
        using var request = CreateLoginRequest(password);
        return await client.SendAsync(request, cancellationToken);
    }

    private static HttpRequestMessage CreateSessionRequest(
        string path,
        string refreshCookie,
        string csrfCookie,
        string csrfHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(
            "Cookie",
            $"__Host-fullnet-refresh={refreshCookie}; fullnet-csrf={csrfCookie}");
        request.Headers.Add("X-CSRF-Token", csrfHeader);
        return request;
    }

    private static string ExtractCookie(IEnumerable<string> cookies, string name)
    {
        var cookie = cookies.Single(value => value.StartsWith(
            $"{name}=",
            StringComparison.Ordinal));
        return cookie.Split(';', 2)[0][(name.Length + 1)..];
    }
}
