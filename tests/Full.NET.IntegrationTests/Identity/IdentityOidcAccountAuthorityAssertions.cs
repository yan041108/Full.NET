using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcAccountAuthorityAssertions
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
        // 各场景使用独立客户端，避免 OIDC 中心会话 Cookie 污染后续 authorize。
        await VerifyDisableInvalidatesOidcTokensAsync(factory, cancellationToken);
        await VerifyPasswordResetInvalidatesOidcTokensAsync(factory, cancellationToken);
    }

    private static async Task VerifyDisableInvalidatesOidcTokensAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var username = $"oidc-disable-{Guid.NewGuid():N}";
        var password = FullNetApiFactory.TestPassword;
        var user = await CreateHostUserAsync(client, adminToken, username, password, cancellationToken);
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            username,
            password,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        await AssertMeAcceptsTokenAsync(client, flow.AccessToken, cancellationToken);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{user.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        await AssertMeRejectsTokenAsync(client, flow.AccessToken, cancellationToken);
        await AssertRefreshRejectedAsync(client, flow, cancellationToken);
    }

    private static async Task VerifyPasswordResetInvalidatesOidcTokensAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var username = $"oidc-reset-{Guid.NewGuid():N}";
        var originalPassword = FullNetApiFactory.TestPassword;
        var newPassword = $"{originalPassword}-rotated";
        var user = await CreateHostUserAsync(client, adminToken, username, originalPassword, cancellationToken);
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            username,
            originalPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        await AssertMeAcceptsTokenAsync(client, flow.AccessToken, cancellationToken);

        using var resetRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{user.Id:D}/reset-password",
            adminToken,
            new ResetHostUserPasswordRequest(newPassword));
        using var resetResponse = await client.SendAsync(resetRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, resetResponse.StatusCode);

        await AssertMeRejectsTokenAsync(client, flow.AccessToken, cancellationToken);
        await AssertRefreshRejectedAsync(client, flow, cancellationToken);
    }

    private static async Task<HostUserResponse> CreateHostUserAsync(
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
            new CreateHostUserRequest(username, "OIDC authority test user", password));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task AssertMeAcceptsTokenAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertMeRejectsTokenAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task AssertRefreshRejectedAsync(
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
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
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