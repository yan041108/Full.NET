using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.IntegrationTests.Identity;

internal static class LocalePreferenceTests
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await LoginAsync(client, cancellationToken);
        AssertLocaleClaimsAreAbsent(token.AccessToken);

        var current = await GetCurrentUserAsync(
            client,
            token.AccessToken,
            cancellationToken);
        Assert.AreEqual("zh-CN", current.PreferredLocale);
        Assert.AreEqual(1, current.ProfileVersion);

        using (var tenantClient = factory.CreateClientForHost("acme.localhost"))
        {
            var tenant = await tenantClient.GetFromJsonAsync<TenantLocaleSnapshot>(
                "/api/v1/tenancy/current",
                cancellationToken);
            Assert.IsNotNull(tenant);
            Assert.AreEqual("zh-CN", tenant.DefaultLocale);
        }

        using (var unsupported = CreateLocaleRequest(
            token.AccessToken,
            new { locale = "fr-FR", profileVersion = 1 }))
        using (var unsupportedResponse = await client.SendAsync(
            unsupported,
            cancellationToken))
        {
            await AssertProblemAsync(
                unsupportedResponse,
                HttpStatusCode.BadRequest,
                "localization.unsupported_locale",
                cancellationToken);
        }

        using (var untrusted = CreateLocaleRequest(
            token.AccessToken,
            new
            {
                locale = "en-US",
                profileVersion = 1,
                userId = Guid.NewGuid(),
                tenantId = Guid.NewGuid(),
                scopeKey = "tenant:untrusted",
            }))
        using (var untrustedResponse = await client.SendAsync(
            untrusted,
            cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, untrustedResponse.StatusCode);
        }

        LocalePreferenceSnapshot updated;
        using (var update = CreateLocaleRequest(
            token.AccessToken,
            new { locale = "en-GB", profileVersion = 1 }))
        using (var updateResponse = await client.SendAsync(update, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
            updated = (await updateResponse.Content
                .ReadFromJsonAsync<LocalePreferenceSnapshot>(cancellationToken))!;
            Assert.IsNotNull(updated);
            Assert.AreEqual("en-US", updated.PreferredLocale);
            Assert.AreEqual(2, updated.ProfileVersion);
        }

        current = await GetCurrentUserAsync(client, token.AccessToken, cancellationToken);
        Assert.AreEqual("en-US", current.PreferredLocale);
        Assert.AreEqual(2, current.ProfileVersion);

        using (var stale = CreateLocaleRequest(
            token.AccessToken,
            new { locale = "zh-CN", profileVersion = 1 }))
        using (var staleResponse = await client.SendAsync(stale, cancellationToken))
        {
            await AssertProblemAsync(
                staleResponse,
                HttpStatusCode.Conflict,
                "identity.profile_version_conflict",
                cancellationToken);
        }

        var tenantToken = await EnterTenantAsync(
            client,
            token.AccessToken,
            cancellationToken);
        AssertLocaleClaimsAreAbsent(tenantToken);
        current = await GetCurrentUserAsync(client, tenantToken, cancellationToken);
        Assert.AreEqual("en-US", current.PreferredLocale);
        Assert.AreEqual(2, current.ProfileVersion);
    }

    private static async Task<TokenResponse> LoginAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                username = "admin",
                password = FullNetApiFactory.TestPassword,
            }),
        };
        request.Headers.Add("Origin", "http://localhost");
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken))!;
    }

    private static async Task<CurrentUserLocaleSnapshot> GetCurrentUserAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerRequest(HttpMethod.Get, "/api/v1/me", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content
            .ReadFromJsonAsync<CurrentUserLocaleSnapshot>(cancellationToken))!;
    }

    private static async Task<string> EnterTenantAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var availableRequest = CreateBearerRequest(
            HttpMethod.Get,
            "/api/v1/tenancy/available",
            accessToken);
        using var availableResponse = await client.SendAsync(
            availableRequest,
            cancellationToken);
        var tenants = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        var tenant = tenants!.Single(item => item.Identifier == "acme");

        using var contextRequest = CreateBearerRequest(
            HttpMethod.Put,
            "/api/v1/tenancy/context",
            accessToken);
        contextRequest.Content = JsonContent.Create(new { tenantId = tenant.Id });
        using var contextResponse = await client.SendAsync(
            contextRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, contextResponse.StatusCode);
        var context = await contextResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        return context!.AccessToken;
    }

    private static HttpRequestMessage CreateLocaleRequest(
        string accessToken,
        object body)
    {
        var request = CreateBearerRequest(
            HttpMethod.Put,
            "/api/v1/me/locale",
            accessToken);
        request.Content = JsonContent.Create(body);
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

    private static void AssertLocaleClaimsAreAbsent(string accessToken)
    {
        var jwt = new JsonWebToken(accessToken);
        Assert.IsFalse(jwt.Claims.Any(claim =>
            claim.Type.Contains("locale", StringComparison.OrdinalIgnoreCase)
            || claim.Type.Contains("profileversion", StringComparison.OrdinalIgnoreCase)
            || claim.Type.Contains("profile_version", StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        CancellationToken cancellationToken)
    {
        Assert.AreEqual(expectedStatus, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            expectedCode,
            problem.RootElement.GetProperty("code").GetString());
    }

    private sealed record CurrentUserLocaleSnapshot(
        string PreferredLocale,
        int ProfileVersion);

    private sealed record LocalePreferenceSnapshot(
        string PreferredLocale,
        int ProfileVersion);

    private sealed record TenantLocaleSnapshot(string DefaultLocale);
}
