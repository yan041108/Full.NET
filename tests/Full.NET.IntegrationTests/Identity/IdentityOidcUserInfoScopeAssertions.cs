using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcUserInfoScopeAssertions
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
        using var client = factory.CreateClientForHost("localhost");

        var openIdOnlyFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            scopes: "openid",
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(openIdOnlyFlow.AccessToken));
        foreach (var token in new[] { openIdOnlyFlow.AccessToken, openIdOnlyFlow.IdToken! })
        {
            Assert.IsNull(IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(token, "name"));
            Assert.IsNull(IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(token, "preferred_username"));
        }
        using (var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"))
        {
            userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                openIdOnlyFlow.AccessToken);
            using var userInfoResponse = await client.SendAsync(userInfoRequest, cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, userInfoResponse.StatusCode);
            using var document = JsonDocument.Parse(
                await userInfoResponse.Content.ReadAsStringAsync(cancellationToken));
            Assert.IsTrue(document.RootElement.TryGetProperty("sub", out var subject));
            Assert.IsFalse(string.IsNullOrWhiteSpace(subject.GetString()));
            Assert.IsFalse(document.RootElement.TryGetProperty("name", out _));
            Assert.IsFalse(document.RootElement.TryGetProperty("preferred_username", out _));
        }

        var profileFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            scopes: "openid profile",
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(profileFlow.AccessToken));
        foreach (var token in new[] { profileFlow.AccessToken, profileFlow.IdToken! })
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(token, "name")));
            Assert.AreEqual("admin", IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(token, "preferred_username"));
        }
        using var profileUserInfoRequest = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        profileUserInfoRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            profileFlow.AccessToken);
        using var profileUserInfoResponse = await client.SendAsync(profileUserInfoRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, profileUserInfoResponse.StatusCode);
        using var profileDocument = JsonDocument.Parse(
            await profileUserInfoResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.IsTrue(profileDocument.RootElement.TryGetProperty("sub", out _));
        Assert.IsTrue(profileDocument.RootElement.TryGetProperty("name", out var name));
        Assert.IsFalse(string.IsNullOrWhiteSpace(name.GetString()));
        Assert.IsTrue(profileDocument.RootElement.TryGetProperty("preferred_username", out var username));
        Assert.AreEqual("admin", username.GetString());
    }
}
