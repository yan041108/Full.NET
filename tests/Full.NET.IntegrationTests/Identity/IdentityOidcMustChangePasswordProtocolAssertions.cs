using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.WebUtilities;

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
        await AssertAuthorizeRejectsMustChangePasswordUserAsync(
            client,
            username,
            password,
            cancellationToken);

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

    private static async Task AssertAuthorizeRejectsMustChangePasswordUserAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var state = Guid.NewGuid().ToString("N");
        var nonce = Guid.NewGuid().ToString("N");
        var (verifier, challenge) = IdentityOidcRelyingPartyFixture.CreatePkcePair();
        const string scopes = "openid profile offline_access";
        var authorizeUrl = "/connect/authorize"
            + $"?client_id={Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicRedirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(scopes)}"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&nonce={Uri.EscapeDataString(nonce)}"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + "&code_challenge_method=S256";
        using var authorizeGet = await client.GetAsync(authorizeUrl, cancellationToken);
        Assert.IsTrue(
            authorizeGet.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect
                or HttpStatusCode.Found or HttpStatusCode.SeeOther,
            $"Authorize GET returned {authorizeGet.StatusCode}.");

        var loginPage = await authorizeGet.Content.ReadAsStringAsync(cancellationToken);
        const string tokenPrefix = "name=\"__RequestVerificationToken\" value=\"";
        var tokenStart = loginPage.IndexOf(tokenPrefix, StringComparison.Ordinal);
        Assert.IsTrue(tokenStart >= 0, "中心登录页缺少防伪令牌。");
        tokenStart += tokenPrefix.Length;
        var tokenEnd = loginPage.IndexOf('"', tokenStart);
        var antiforgeryToken = System.Net.WebUtility.HtmlDecode(loginPage[tokenStart..tokenEnd]);

        using var authorizePost = new HttpRequestMessage(HttpMethod.Post, "/connect/authorize")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = IdentityOidcRelyingPartyFixture.PublicClientId,
                ["redirect_uri"] = IdentityOidcRelyingPartyFixture.PublicRedirectUri,
                ["response_type"] = "code",
                ["scope"] = scopes,
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["username"] = username,
                ["password"] = password,
                ["__RequestVerificationToken"] = antiforgeryToken,
            }),
        };
        using var authorizePostResponse = await client.SendAsync(authorizePost, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Redirect, authorizePostResponse.StatusCode);
        var location = authorizePostResponse.Headers.Location;
        Assert.IsNotNull(location);
        Assert.AreEqual(IdentityOidcRelyingPartyFixture.PublicRedirectUri, location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.AreEqual("access_denied", query["error"].ToString());
        Assert.AreEqual(state, query["state"].ToString());
        Assert.IsFalse(query.ContainsKey("code"), "首次改密前不得签发授权码。");
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
