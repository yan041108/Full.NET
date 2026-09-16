using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcMustChangePasswordProtocolAssertions
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
        await VerifyMustChangePasswordRejectsProtocolGrantsAsync(factory, cancellationToken);
    }

    private static async Task VerifyMustChangePasswordRejectsProtocolGrantsAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var username = $"oidc-must-change-{Guid.NewGuid():N}";
        var password = FullNetApiFactory.TestPassword;
        await CreateHostUserAsync(client, adminToken, username, password, cancellationToken);
        var pending = await IdentityOidcRelyingPartyFixture.BeginAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            username,
            password,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);

        await AssertAuthorizationCodeExchangeRejectedAsync(client, pending, cancellationToken);

        using var clearedClient = factory.CreateClientForHost("localhost");
        _ = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            clearedClient,
            username,
            password,
            cancellationToken);
        var clearedFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            clearedClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            username,
            IntegrationTestAuthHelper.ClearedPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        await AssertUserInfoAcceptsTokenAsync(clearedClient, clearedFlow.AccessToken, cancellationToken);
        await AssertRefreshAcceptsTokenAsync(clearedClient, clearedFlow, cancellationToken);
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
            exchangeResult.RawTokenResponse.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase),
            $"Expected authorization code exchange to fail while password change is required, got: {exchangeResult.RawTokenResponse}");
    }

    private static async Task CreateHostUserAsync(
        HttpClient client,
        string adminToken,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "OIDC must-change test user", password));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
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

    private static async Task AssertRefreshAcceptsTokenAsync(
        HttpClient client,
        IdentityOidcAuthorizationResult flow,
        CancellationToken cancellationToken)
    {
        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsTrue(refreshResult.IsSuccessStatusCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(refreshResult.RawBody));
        Assert.IsTrue(
            refreshResult.RawBody.Contains("access_token", StringComparison.OrdinalIgnoreCase),
            $"Expected refresh grant to succeed after password change, got: {refreshResult.RawBody}");
    }

    private static HttpRequestMessage CreateUserInfoRequest(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string bearerToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return request;
    }
}