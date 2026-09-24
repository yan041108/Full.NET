using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcGovernanceAssertions
{
    private const string ExternalRedirectUri = "https://localhost:5012/signin-oidc-governance-external";

    public static async Task VerifyAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        await VerifyV08WithoutOfflineAccessAsync(client, cancellationToken);
        await VerifyV09RefreshTokenReuseAsync(client, cancellationToken);
        await VerifyV11ScopePermissionBoundaryAsync(client, cancellationToken);
        await VerifyV15DisabledClientRejectedAtTokenEndpointAsync(client, cancellationToken);
    }

    private static async Task VerifyV08WithoutOfflineAccessAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var result = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.IsNull(result.RefreshToken);
    }

    private static async Task VerifyV09RefreshTokenReuseAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

        var firstRefresh = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsTrue(firstRefresh.IsSuccessStatusCode);

        var secondRefresh = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(secondRefresh.IsSuccessStatusCode);
        StringAssert.Contains(secondRefresh.RawBody, "error");
    }

    private static async Task VerifyV11ScopePermissionBoundaryAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var externalClientId = $"gov-ext-{Guid.NewGuid():N}"[..24];
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/oidc-clients")
        {
            Content = JsonContent.Create(new CreateOidcClientRequest(
                externalClientId,
                "Governance external client",
                [ExternalRedirectUri],
                [],
                ["openid", "profile"],
                false,
                false,
                null)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);

        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            externalClientId,
            ExternalRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.AccessToken));

        using var usersRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/users?page=1&pageSize=1");
        usersRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var usersResponse = await client.SendAsync(usersRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, usersResponse.StatusCode);
        using var problem = JsonDocument.Parse(await usersResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyV15DisabledClientRejectedAtTokenEndpointAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var clientId = $"gov-disable-{Guid.NewGuid():N}"[..24];
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/oidc-clients")
        {
            Content = JsonContent.Create(new CreateOidcClientRequest(
                clientId,
                "Governance disable client",
                [ExternalRedirectUri],
                [],
                ["openid", "profile", "offline_access"],
                false,
                true,
                null)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateOidcClientResponse>(cancellationToken);
        Assert.IsNotNull(created);

        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            clientId,
            ExternalRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

        using var disableRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/oidc-clients/{created!.Client.Id:D}/disable");
        disableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            clientId,
            null,
            cancellationToken);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
        Assert.IsTrue(
            refreshResult.RawBody.Contains("unauthorized_client", StringComparison.OrdinalIgnoreCase)
                || refreshResult.RawBody.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase),
            $"Expected disabled client refresh to fail, got: {refreshResult.RawBody}");

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var meResponse = await client.SendAsync(meRequest, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            meResponse.StatusCode,
            "Disabled clients must fail closed on resource APIs.");
    }

}
