using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.AreEqual(0L, authorization.PermissionCount);
        Assert.AreEqual(1L, authorization.AssignmentCount);
        using var client = factory.CreateClientForHost("localhost");

        using (var localizationFactory = factory.CreateIsolatedFactory())
        using (var localizationClient = localizationFactory.CreateClientForHost("localhost"))
        {
            await LocalizedProblemDetailsTests.VerifyAsync(
                localizationClient,
                cancellationToken);
        }

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

        using (var rateLimitFactory = factory.CreateIsolatedFactory())
        using (var rateLimitClient = rateLimitFactory.CreateClientForHost("localhost"))
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "/api/v1/auth/refresh");
                request.Headers.Add("Origin", "http://localhost");
                using var response = await rateLimitClient.SendAsync(
                    request,
                    cancellationToken);
                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            }

            using var rejectedRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/auth/refresh");
            rejectedRequest.Headers.Add("Origin", "http://localhost");
            using var rejectedResponse = await rateLimitClient.SendAsync(
                rejectedRequest,
                cancellationToken);
            Assert.AreEqual(
                HttpStatusCode.TooManyRequests,
                rejectedResponse.StatusCode);
            Assert.AreEqual(
                "application/problem+json",
                rejectedResponse.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(
                await rejectedResponse.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(
                "identity.authentication.rate_limited",
                problem.RootElement.GetProperty("code").GetString());
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
        Assert.AreEqual(
            "true",
            jwt.GetClaim(IdentityClaimTypes.SuperAdministrator).Value);
        Assert.IsFalse(jwt.Claims.Any(claim =>
            claim.Type == IdentityClaimTypes.Permission));
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
        Assert.IsTrue(currentUser.IsSuperAdministrator);
        var expectedHostPermissions = factory.Services
            .GetServices<IAuthorizationCatalogContributor>()
            .SelectMany(contributor => contributor.Permissions)
            .Where(permission => (permission.Scope & AuthorizationScope.Host) != 0)
            .Select(permission => permission.Code)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(
            expectedHostPermissions,
            currentUser.Permissions.ToArray());

        using (var anonymousNavigation = await client.GetAsync(
            "/api/v1/navigation",
            cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.Unauthorized,
                anonymousNavigation.StatusCode);
        }
        using (var forbiddenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/navigation"))
        {
            forbiddenRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await factory.CreateHostAccessTokenAsync(
                    ["platform.dashboard.read"],
                    cancellationToken));
            using var forbiddenResponse = await client.SendAsync(
                forbiddenRequest,
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
            using var forbiddenProblem = JsonDocument.Parse(
                await forbiddenResponse.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(
                "authorization.permission_denied",
                forbiddenProblem.RootElement.GetProperty("code").GetString());
        }
        using var navigationRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/navigation");
        navigationRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.AccessToken);
        using var navigationResponse = await client.SendAsync(
            navigationRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, navigationResponse.StatusCode);
        var navigation = await navigationResponse.Content
            .ReadFromJsonAsync<NavigationNodeResponse[]>(cancellationToken);
        Assert.IsNotNull(navigation);
        var expectedRootNavigation = factory.Services
            .GetServices<IAuthorizationCatalogContributor>()
            .SelectMany(contributor => contributor.Navigation)
            .Where(item =>
                item.ParentId is null
                && expectedHostPermissions.Contains(
                    item.RequiredPermission,
                    StringComparer.Ordinal))
            .OrderBy(item => item.Order)
            .ToArray();
        var navigationById = navigation.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        foreach (var expectedNavigation in expectedRootNavigation)
        {
            Assert.IsTrue(
                navigationById.TryGetValue(expectedNavigation.Id, out var actualNavigation),
                $"缺少内置 Host 导航节点 '{expectedNavigation.Id}'。");
            Assert.AreEqual(
                expectedNavigation.ComponentKey,
                actualNavigation.ComponentKey);
        }

        var operatorUserId = Guid.Parse(
            jwt.GetClaim(JwtRegisteredClaimNames.Sub).Value);
        var targetIdentity = await factory.CreateHostIdentityAsync(
            $"remote-target-{Guid.NewGuid():N}",
            [],
            cancellationToken);
        using (var invalidReauthenticationRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/super-administrators/grant",
            token.AccessToken,
            new GrantSuperAdministratorRequest(
                targetIdentity.Username,
                "wrong-password")))
        using (var invalidReauthenticationResponse = await client.SendAsync(
            invalidReauthenticationRequest,
            cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.Unauthorized,
                invalidReauthenticationResponse.StatusCode);
        }

        using (var grantRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/super-administrators/grant",
            token.AccessToken,
            new GrantSuperAdministratorRequest(
                targetIdentity.Username,
                FullNetApiFactory.TestPassword)))
        using (var grantResponse = await client.SendAsync(
            grantRequest,
            cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, grantResponse.StatusCode);
            var grant = await grantResponse.Content
                .ReadFromJsonAsync<SuperAdministratorChangeResponse>(
                    cancellationToken);
            Assert.IsNotNull(grant);
            Assert.AreEqual(targetIdentity.UserId, grant.TargetUserId);
            Assert.IsTrue(grant.Changed);
        }

        using (var targetOldTokenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/navigation"))
        {
            targetOldTokenRequest.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    targetIdentity.AccessToken);
            using var targetOldTokenResponse = await client.SendAsync(
                targetOldTokenRequest,
                cancellationToken);
            Assert.AreEqual(
                HttpStatusCode.Unauthorized,
                targetOldTokenResponse.StatusCode);
        }

        using (var administratorsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/super-administrators/"))
        {
            administratorsRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var administratorsResponse = await client.SendAsync(
                administratorsRequest,
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, administratorsResponse.StatusCode);
            var administrators = await administratorsResponse.Content
                .ReadFromJsonAsync<SuperAdministratorResponse[]>(
                    cancellationToken);
            Assert.IsNotNull(administrators);
            CollectionAssert.Contains(
                administrators.Select(item => item.UserId).ToArray(),
                targetIdentity.UserId);
        }

        using (var auditsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/super-administrators/audits?limit=20"))
        {
            auditsRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var auditsResponse = await client.SendAsync(
                auditsRequest,
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, auditsResponse.StatusCode);
            var audits = await auditsResponse.Content
                .ReadFromJsonAsync<SuperAdministratorAuditResponse[]>(
                    cancellationToken);
            Assert.IsNotNull(audits);
            Assert.IsTrue(audits.Any(item =>
                item.TargetUserId == targetIdentity.UserId
                && item.ActorUserId == operatorUserId
                && item.EventType == "identity.super_administrator.granted"));
        }

        using (var revokeRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/super-administrators/{targetIdentity.UserId:D}/revoke",
            token.AccessToken,
            new RevokeSuperAdministratorRequest(FullNetApiFactory.TestPassword)))
        using (var revokeResponse = await client.SendAsync(
            revokeRequest,
            cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, revokeResponse.StatusCode);
        }

        using (var lastAdministratorRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/super-administrators/{operatorUserId:D}/revoke",
            token.AccessToken,
            new RevokeSuperAdministratorRequest(FullNetApiFactory.TestPassword)))
        using (var lastAdministratorResponse = await client.SendAsync(
            lastAdministratorRequest,
            cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, lastAdministratorResponse.StatusCode);
            using var lastAdministratorProblem = JsonDocument.Parse(
                await lastAdministratorResponse.Content.ReadAsStringAsync(
                    cancellationToken));
            Assert.AreEqual(
                IdentityErrorCodes.SuperAdministratorLastRemaining,
                lastAdministratorProblem.RootElement.GetProperty("code").GetString());
        }

        var refreshCookie = ExtractCookie(cookies, "__Host-fullnet-refresh");
        var csrfCookie = ExtractCookie(cookies, "fullnet-csrf");
        using (var untrustedOriginRequest = CreateSessionRequest(
            "/api/v1/auth/refresh",
            "untrusted-refresh-token",
            csrfCookie,
            csrfCookie,
            "https://untrusted.example"))
        using (var untrustedOriginResponse = await client.SendAsync(
            untrustedOriginRequest,
            cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, untrustedOriginResponse.StatusCode);
            using var problem = JsonDocument.Parse(
                await untrustedOriginResponse.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(
                "identity.origin_not_allowed",
                problem.RootElement.GetProperty("code").GetString());
        }

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

        using (var consumedSessionRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/navigation"))
        {
            consumedSessionRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var consumedSessionResponse = await client.SendAsync(
                consumedSessionRequest,
                cancellationToken);
            Assert.AreEqual(
                HttpStatusCode.Unauthorized,
                consumedSessionResponse.StatusCode);
            using var consumedSessionProblem = JsonDocument.Parse(
                await consumedSessionResponse.Content.ReadAsStringAsync(
                    cancellationToken));
            Assert.AreEqual(
                IdentityErrorCodes.SessionNotActive,
                consumedSessionProblem.RootElement.GetProperty("code").GetString());
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

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
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
        string csrfHeader,
        string origin = "http://localhost")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(
            "Cookie",
            $"__Host-fullnet-refresh={refreshCookie}; fullnet-csrf={csrfCookie}");
        request.Headers.Add("X-CSRF-Token", csrfHeader);
        request.Headers.Add("Origin", origin);
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
