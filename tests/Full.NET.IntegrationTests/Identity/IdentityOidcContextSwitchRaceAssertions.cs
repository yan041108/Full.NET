using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcContextSwitchRaceAssertions
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
        await VerifyConcurrentContextSwitchViaHttpAsync(factory, cancellationToken);
        await VerifyConcurrentRefreshAndContextSwitchViaHttpAsync(factory, cancellationToken);
    }

    private static async Task VerifyConcurrentContextSwitchViaHttpAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var hostClient = factory.CreateClientForHost("localhost");
        using var acmeClient = factory.CreateClientForHost("acme.localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            hostClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        var acmeTenant = await GetAcmeTenantAsync(hostClient, flow.AccessToken, cancellationToken);

        var firstTask = hostClient.SendAsync(
            CreateContextSwitchRequest(acmeTenant.Id, flow.AccessToken),
            cancellationToken);
        var secondTask = hostClient.SendAsync(
            CreateContextSwitchRequest(acmeTenant.Id, flow.AccessToken),
            cancellationToken);
        await Task.WhenAll(firstTask, secondTask);
        using var firstResponse = await firstTask;
        using var secondResponse = await secondTask;

        var statuses = new[] { firstResponse.StatusCode, secondResponse.StatusCode };
        Assert.AreEqual(1, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.AreEqual(1, statuses.Count(status => status == HttpStatusCode.Conflict));

        var conflictResponse = firstResponse.StatusCode == HttpStatusCode.Conflict
            ? firstResponse
            : secondResponse;
        using var problem = JsonDocument.Parse(
            await conflictResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.SessionContextConflict,
            problem.RootElement.GetProperty("code").GetString());

        var successResponse = firstResponse.StatusCode == HttpStatusCode.OK
            ? firstResponse
            : secondResponse;
        var switched = await successResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(switched);
        Assert.AreEqual(acmeTenant.Id, switched.Context.TenantId);
        await AssertMeAcceptsTokenAsync(
            acmeClient,
            switched.AccessToken,
            "OIDC tenant token after winning concurrent context switch",
            cancellationToken);
        await AssertMeRejectsTokenAsync(
            hostClient,
            flow.AccessToken,
            "OIDC host token after concurrent context switch",
            cancellationToken);
    }

    private static async Task VerifyConcurrentRefreshAndContextSwitchViaHttpAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var hostClient = factory.CreateClientForHost("localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            hostClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));
        var acmeTenant = await GetAcmeTenantAsync(hostClient, flow.AccessToken, cancellationToken);

        var refreshTask = IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            hostClient,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        var contextTask = hostClient.SendAsync(
            CreateContextSwitchRequest(acmeTenant.Id, flow.AccessToken),
            cancellationToken);
        await Task.WhenAll(refreshTask, contextTask);
        var refreshResult = await refreshTask;
        using var contextResponse = await contextTask;

        Assert.AreNotEqual(HttpStatusCode.InternalServerError, contextResponse.StatusCode);
        Assert.IsTrue(
            refreshResult.IsSuccessStatusCode
                || contextResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Concurrent OIDC refresh and context switch must not fail open, refresh={refreshResult.RawBody}, context={contextResponse.StatusCode}");

        var recoveryFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            hostClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        using var switchRequest = CreateContextSwitchRequest(acmeTenant.Id, recoveryFlow.AccessToken);
        using var switchResponse = await hostClient.SendAsync(switchRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, switchResponse.StatusCode);
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