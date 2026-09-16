using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcRefreshLifecycleAssertions
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
        await VerifyRefreshPreservesApplicationSessionBindingAsync(factory, cancellationToken);
    }

    private static async Task VerifyRefreshPreservesApplicationSessionBindingAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
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

        var initialBinding = ReadOidcAccessTokenBinding(flow.AccessToken);
        var firstRefresh = await RefreshAndParseAsync(
            client,
            flow.RefreshToken!,
            cancellationToken);
        AssertRefreshBindingPreserved(initialBinding, firstRefresh.Binding);
        await AssertUserInfoAcceptsTokenAsync(client, firstRefresh.AccessToken, cancellationToken);
        await AssertMeReturnsSessionAsync(client, firstRefresh.AccessToken, initialBinding.ApplicationSessionId, cancellationToken);

        var secondRefresh = await RefreshAndParseAsync(
            client,
            firstRefresh.RefreshToken,
            cancellationToken);
        AssertRefreshBindingPreserved(initialBinding, secondRefresh.Binding);
        await AssertUserInfoAcceptsTokenAsync(client, secondRefresh.AccessToken, cancellationToken);
        await AssertMeReturnsSessionAsync(client, secondRefresh.AccessToken, initialBinding.ApplicationSessionId, cancellationToken);
    }

    private static async Task<(string AccessToken, string RefreshToken, OidcAccessTokenBinding Binding)> RefreshAndParseAsync(
        HttpClient client,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            refreshToken,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsTrue(
            refreshResult.IsSuccessStatusCode,
            $"Expected refresh grant to succeed, got: {refreshResult.RawBody}");
        using var document = JsonDocument.Parse(refreshResult.RawBody);
        var root = document.RootElement;
        var accessToken = root.GetProperty("access_token").GetString();
        var rotatedRefreshToken = root.TryGetProperty("refresh_token", out var refreshElement)
            ? refreshElement.GetString()
            : null;
        Assert.IsFalse(string.IsNullOrWhiteSpace(accessToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(rotatedRefreshToken));
        return (accessToken!, rotatedRefreshToken!, ReadOidcAccessTokenBinding(accessToken!));
    }

    private static OidcAccessTokenBinding ReadOidcAccessTokenBinding(string accessToken)
    {
        var applicationSessionId = Guid.Parse(
            IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
                accessToken,
                FullNetIdentityClaimTypes.ApplicationSessionId)
            ?? throw new InvalidOperationException("OIDC access token is missing application session id."));
        var centerSessionId = Guid.Parse(
            IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
                accessToken,
                FullNetIdentityClaimTypes.CenterSessionId)
            ?? throw new InvalidOperationException("OIDC access token is missing center session id."));
        var tokenUse = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
            accessToken,
            FullNetIdentityClaimTypes.TokenUse)
            ?? string.Empty;
        Assert.AreEqual(
            IdentityOidcPrincipalFactory.TokenUseAccess,
            tokenUse,
            "OIDC resource access tokens must carry access token_use.");
        return new OidcAccessTokenBinding(applicationSessionId, centerSessionId);
    }

    private static void AssertRefreshBindingPreserved(
        OidcAccessTokenBinding original,
        OidcAccessTokenBinding refreshed)
    {
        Assert.AreEqual(
            original.ApplicationSessionId,
            refreshed.ApplicationSessionId,
            "Refresh must keep the same authoritative application session id.");
        Assert.AreEqual(
            original.CenterSessionId,
            refreshed.CenterSessionId,
            "Refresh must keep the same center session binding.");
    }

    private static async Task AssertUserInfoAcceptsTokenAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertMeReturnsSessionAsync(
        HttpClient client,
        string accessToken,
        Guid expectedSessionId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);
        Assert.IsNotNull(profile);
        Assert.AreEqual(expectedSessionId, profile.SessionId);
    }

    private sealed record OidcAccessTokenBinding(Guid ApplicationSessionId, Guid CenterSessionId);
}