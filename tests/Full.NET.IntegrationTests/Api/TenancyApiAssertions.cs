using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Api;

internal static class TenancyApiAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);

        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        using var response = await tenantClient
            .GetAsync("/api/v1/tenancy/current", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var successJson = await response.Content
            .ReadAsStringAsync(cancellationToken);
        var tenant = JsonSerializer.Deserialize<TenantSummary>(
            successJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsNotNull(tenant);
        Assert.AreEqual("acme", tenant.Identifier);
        Assert.AreEqual("acme.localhost", tenant.Domain);
        using (var successDocument = JsonDocument.Parse(successJson))
        {
            Assert.IsFalse(successDocument.RootElement.TryGetProperty("success", out _));
            Assert.IsFalse(successDocument.RootElement.TryGetProperty("code", out _));
            Assert.IsFalse(successDocument.RootElement.TryGetProperty("data", out _));
        }

        using var missingClient = factory.CreateClientForHost("missing.localhost");
        using var missingResponse = await missingClient
            .GetAsync("/api/v1/tenancy/current", cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await missingResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "tenancy.host-not-found",
            problem.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("traceId").GetString()));

        await VerifyHostTenantContextFlowAsync(factory, cancellationToken);
    }

    private static async Task VerifyHostTenantContextFlowAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                username = "admin",
                password = FullNetApiFactory.TestPassword,
            }),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(
            loginRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(loginToken);
        var loginCookies = loginResponse.Headers.GetValues("Set-Cookie").ToArray();
        var refreshCookie = ExtractCookie(loginCookies, "__Host-fullnet-refresh");
        var csrfCookie = ExtractCookie(loginCookies, "fullnet-csrf");

        using var availableRequest = CreateBearerRequest(
            HttpMethod.Get,
            "/api/v1/tenancy/available",
            loginToken.AccessToken);
        using var availableResponse = await client.SendAsync(
            availableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        var acme = available.Single(tenant => tenant.Identifier == "acme");
        Assert.AreEqual("Acme Corporation", acme.Name);

        using (var hostTokenOnTenantClient = factory.CreateClientForHost(
            "acme.localhost"))
        using (var hostTokenOnTenantRequest = CreateBearerRequest(
            HttpMethod.Get,
            "/api/v1/tenancy/available",
            loginToken.AccessToken))
        using (var hostTokenOnTenantResponse = await hostTokenOnTenantClient.SendAsync(
            hostTokenOnTenantRequest,
            cancellationToken))
        {
            await AssertContextMismatchAsync(
                hostTokenOnTenantResponse,
                cancellationToken);
        }

        using (var missingRequest = CreateContextRequest(
            Guid.NewGuid(),
            loginToken.AccessToken))
        using (var missingResponse = await client.SendAsync(
            missingRequest,
            cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
            using var missingProblem = JsonDocument.Parse(
                await missingResponse.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(
                "tenancy.context_not_found",
                missingProblem.RootElement.GetProperty("code").GetString());
        }

        using var enterRequest = CreateContextRequest(
            acme.Id,
            loginToken.AccessToken);
        using var enterResponse = await client.SendAsync(
            enterRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
        var entered = await enterResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(entered);
        Assert.AreEqual(acme.Id, entered.Context.TenantId);
        Assert.AreEqual($"tenant:{acme.Id:N}", entered.Context.Scope);

        using (var matchingTenantClient = factory.CreateClientForHost(
            "acme.localhost"))
        using (var matchingTenantRequest = CreateBearerRequest(
            HttpMethod.Get,
            "/api/v1/tenancy/current",
            entered.AccessToken))
        using (var matchingTenantResponse = await matchingTenantClient.SendAsync(
            matchingTenantRequest,
            cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, matchingTenantResponse.StatusCode);
        }

        using (var mismatchedTenantClient = factory.CreateClientForHost(
            "missing.localhost"))
        using (var mismatchedTenantRequest = CreateBearerRequest(
            HttpMethod.Get,
            "/api/v1/tenancy/current",
            entered.AccessToken))
        using (var mismatchedTenantResponse = await mismatchedTenantClient.SendAsync(
            mismatchedTenantRequest,
            cancellationToken))
        {
            await AssertContextMismatchAsync(
                mismatchedTenantResponse,
                cancellationToken);
        }

        using var refreshRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/refresh");
        refreshRequest.Headers.Add(
            "Cookie",
            $"__Host-fullnet-refresh={refreshCookie}; fullnet-csrf={csrfCookie}");
        refreshRequest.Headers.Add("Origin", "http://localhost");
        refreshRequest.Headers.Add("X-CSRF-Token", csrfCookie);
        using var refreshResponse = await client.SendAsync(
            refreshRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(refreshed);
        using var refreshedJwt = JsonDocument.Parse(
            DecodeJwtPayload(refreshed.AccessToken));
        Assert.AreEqual(
            acme.Id.ToString("D"),
            refreshedJwt.RootElement.GetProperty("fullnet_tenant_id").GetString());

        using var returnHostRequest = CreateContextRequest(
            null,
            refreshed.AccessToken);
        using var returnHostResponse = await client.SendAsync(
            returnHostRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, returnHostResponse.StatusCode);
        var returned = await returnHostResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(returned);
        Assert.IsNull(returned.Context.TenantId);
        Assert.AreEqual("host", returned.Context.Scope);
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

    private static async Task AssertContextMismatchAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "tenancy.context_mismatch",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static string DecodeJwtPayload(string accessToken)
    {
        var encoded = accessToken.Split('.')[1]
            .Replace('-', '+')
            .Replace('_', '/');
        encoded = encoded.PadRight(
            encoded.Length + ((4 - encoded.Length % 4) % 4),
            '=');
        return System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(encoded));
    }
}
