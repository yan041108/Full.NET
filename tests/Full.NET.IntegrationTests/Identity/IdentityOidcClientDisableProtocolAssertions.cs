using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcClientDisableProtocolAssertions
{
    private const string ExternalRedirectUri = "https://localhost:5014/signin-oidc-client-disable";

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
        await VerifyDisabledClientRejectsProtocolGrantsAsync(factory, cancellationToken);
    }

    private static async Task VerifyDisabledClientRejectsProtocolGrantsAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var clientId = $"proto-disable-{Guid.NewGuid():N}"[..24];
        var created = await CreateExternalClientAsync(client, adminToken, clientId, cancellationToken);
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            clientId,
            ExternalRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        await AssertUserInfoAcceptsTokenAsync(client, flow.AccessToken, cancellationToken);
        var pending = await IdentityOidcRelyingPartyFixture.BeginAuthorizationCodeFlowAsync(
            client,
            clientId,
            ExternalRedirectUri,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);

        using var disableRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/oidc-clients/{created.Client.Id:D}/disable");
        disableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        await AssertProtocolRejectsAfterClientDisableAsync(client, clientId, flow, pending, cancellationToken);
    }

    private static async Task AssertProtocolRejectsAfterClientDisableAsync(
        HttpClient client,
        string clientId,
        IdentityOidcAuthorizationResult flow,
        IdentityOidcAuthorizationCodePending pending,
        CancellationToken cancellationToken)
    {
        await AssertUserInfoRejectsTokenAsync(client, flow.AccessToken, cancellationToken);
        await AssertRefreshRejectedAsync(client, flow, clientId, cancellationToken);
        await AssertAuthorizationCodeExchangeRejectedAsync(client, pending, cancellationToken);
        await AssertAuthorizeRejectsDisabledClientAsync(client, clientId, cancellationToken);
    }

    private static async Task AssertAuthorizeRejectsDisabledClientAsync(
        HttpClient client,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var authorizeResponse = await client.GetAsync(BuildAuthorizeUrl(clientId), cancellationToken);
        Assert.IsTrue(
            authorizeResponse.Headers.Location is not null,
            "Disabled clients must redirect authorize failures to the relying party.");
        StringAssert.Contains(
            authorizeResponse.Headers.Location!.ToString(),
            "error=unauthorized_client");
    }

    private static async Task AssertAuthorizationCodeExchangeRejectedAsync(
        HttpClient client,
        IdentityOidcAuthorizationCodePending pending,
        CancellationToken cancellationToken)
    {
        var exchangeResult = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(
            client,
            pending.Code,
            pending.Verifier,
            pending.ClientId,
            pending.RedirectUri,
            null,
            null,
            pending.Nonce,
            null,
            cancellationToken);
        Assert.IsTrue(string.IsNullOrWhiteSpace(exchangeResult.AccessToken));
        Assert.IsTrue(
            exchangeResult.RawTokenResponse.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
                || exchangeResult.RawTokenResponse.Contains("unauthorized_client", StringComparison.OrdinalIgnoreCase),
            $"Expected pending authorization code exchange to fail for disabled client, got: {exchangeResult.RawTokenResponse}");
    }

    private static async Task<CreateOidcClientResponse> CreateExternalClientAsync(
        HttpClient client,
        string adminToken,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/oidc-clients")
        {
            Content = JsonContent.Create(new CreateOidcClientRequest(
                clientId,
                "Protocol disable client",
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
        return created;
    }

    private static async Task AssertUserInfoAcceptsTokenAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateUserInfoRequest(accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertUserInfoRejectsTokenAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateUserInfoRequest(accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.IsTrue(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"UserInfo must reject tokens after client disable, got {(int)response.StatusCode}.");
    }

    private static async Task AssertRefreshRejectedAsync(
        HttpClient client,
        IdentityOidcAuthorizationResult flow,
        string clientId,
        CancellationToken cancellationToken)
    {
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
            $"Expected refresh grant to fail for disabled client, got: {refreshResult.RawBody}");
    }

    private static HttpRequestMessage CreateUserInfoRequest(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static string BuildAuthorizeUrl(string clientId)
    {
        var (_, challenge) = IdentityOidcRelyingPartyFixture.CreatePkcePair();
        return "/connect/authorize"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(ExternalRedirectUri)}"
            + "&response_type=code&scope=openid%20profile%20offline_access"
            + "&state=state&nonce=nonce"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + "&code_challenge_method=S256";
    }
}