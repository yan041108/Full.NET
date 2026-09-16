using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcContextSwitchRefreshInvalidationAssertions
{
    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        using var factory = new FullNetApiFactory(
            provider,
            connectionString,
            IdentityOidcProtocolAssertions.Settings);
        await factory.InitializeAsync(cancellationToken);
        await VerifyPreSwitchRefreshRejectedAfterTenantSwitchAsync(factory, cancellationToken);
    }

    private static async Task VerifyPreSwitchRefreshRejectedAfterTenantSwitchAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var hostClient = factory.CreateClientForHost("localhost");
        using var acmeClient = factory.CreateClientForHost("acme.localhost");
        var publicFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            hostClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(publicFlow.RefreshToken));
        var confidentialFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            hostClient,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(confidentialFlow.RefreshToken));

        var acmeTenant = await GetAcmeTenantAsync(hostClient, publicFlow.AccessToken, cancellationToken);
        using var publicSwitchRequest = CreateContextSwitchRequest(acmeTenant.Id, publicFlow.AccessToken);
        using var publicSwitchResponse = await hostClient.SendAsync(publicSwitchRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publicSwitchResponse.StatusCode);
        var publicSwitched = await publicSwitchResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(publicSwitched);
        Assert.IsFalse(string.IsNullOrWhiteSpace(publicSwitched.RefreshToken));

        using var confidentialSwitchRequest = CreateContextSwitchRequest(
            acmeTenant.Id,
            confidentialFlow.AccessToken);
        using var confidentialSwitchResponse = await hostClient.SendAsync(
            confidentialSwitchRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, confidentialSwitchResponse.StatusCode);
        var confidentialSwitched = await confidentialSwitchResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(confidentialSwitched);
        Assert.IsFalse(string.IsNullOrWhiteSpace(confidentialSwitched.RefreshToken));

        await AssertMeAcceptsTokenAsync(
            acmeClient,
            publicSwitched.AccessToken,
            "public OIDC tenant token after context switch",
            cancellationToken);
        await AssertMeAcceptsTokenAsync(
            acmeClient,
            confidentialSwitched.AccessToken,
            "confidential OIDC tenant token after context switch",
            cancellationToken);
        await AssertRefreshRejectedAsync(
            hostClient,
            publicFlow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            "public OIDC refresh before tenant switch",
            cancellationToken);
        await AssertRefreshRejectedAsync(
            hostClient,
            confidentialFlow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            "confidential OIDC refresh before tenant switch",
            cancellationToken);
        await AssertMeRejectsTokenAsync(
            hostClient,
            publicFlow.AccessToken,
            "public OIDC host access token after tenant switch",
            cancellationToken);
        await AssertMeRejectsTokenAsync(
            hostClient,
            confidentialFlow.AccessToken,
            "confidential OIDC host access token after tenant switch",
            cancellationToken);
    }

    private static async Task<TenantContextSummary> GetAcmeTenantAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var available = await response.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        return available.Single(tenant => tenant.Identifier == "acme");
    }

    private static async Task AssertRefreshRejectedAsync(
        HttpClient client,
        string refreshToken,
        string clientId,
        string? clientSecret,
        string scenario,
        CancellationToken cancellationToken)
    {
        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            refreshToken,
            clientId,
            clientSecret,
            cancellationToken);
        Assert.IsFalse(
            refreshResult.IsSuccessStatusCode,
            $"Pre-switch refresh token must be rejected for {scenario}, got: {refreshResult.RawBody}");
        Assert.IsTrue(
            refreshResult.RawBody.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase),
            $"Expected invalid_grant for {scenario}, got: {refreshResult.RawBody}");
    }

    private static async Task AssertMeAcceptsTokenAsync(
        HttpClient client,
        string accessToken,
        string scenario,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            $"Access token must remain valid for {scenario}.");
    }

    private static async Task AssertMeRejectsTokenAsync(
        HttpClient client,
        string accessToken,
        string scenario,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            response.StatusCode,
            $"Stale OIDC access token must be rejected for {scenario}.");
    }

    private static HttpRequestMessage CreateContextSwitchRequest(
        Guid tenantId,
        string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(tenantId)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}