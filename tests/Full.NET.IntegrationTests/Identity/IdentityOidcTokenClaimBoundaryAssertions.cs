using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcTokenClaimBoundaryAssertions
{
    private const string ExternalRedirectUri = "https://localhost:5013/signin-oidc-claim-boundary";
    private const int MaxExternalAccessTokenLength = 8192;
    private const int MaxExternalAccessTokenClaimCount = 24;

    private static readonly string[] ForbiddenExternalClaims =
    [
        FullNetIdentityClaimTypes.SecurityStamp,
        FullNetIdentityClaimTypes.Permission,
        FullNetIdentityClaimTypes.SuperAdministrator,
    ];

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

        var externalClientId = $"claim-ext-{Guid.NewGuid():N}"[..24];
        await CreateExternalClientAsync(client, externalClientId, cancellationToken);
        var externalFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            externalClientId,
            ExternalRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        AssertExternalTokenOmitsInternalClaims(externalFlow.AccessToken, "access token");
        AssertExternalTokenProfile(externalFlow.AccessToken, "access token");
        if (!string.IsNullOrWhiteSpace(externalFlow.IdToken))
        {
            AssertExternalTokenOmitsInternalClaims(externalFlow.IdToken, "id token");
            AssertExternalTokenProfile(externalFlow.IdToken, "id token");
        }

        var firstPartyFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        AssertFirstPartyAccessTokenIncludesSecurityStamp(firstPartyFlow.AccessToken);
    }

    private static async Task CreateExternalClientAsync(
        HttpClient client,
        string clientId,
        CancellationToken cancellationToken)
    {
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/oidc-clients")
        {
            Content = JsonContent.Create(new CreateOidcClientRequest(
                clientId,
                "Claim boundary external client",
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
    }

    private static void AssertExternalTokenOmitsInternalClaims(string jwt, string tokenKind)
    {
        var payload = ReadJwtPayload(jwt);
        foreach (var claim in ForbiddenExternalClaims)
        {
            Assert.IsFalse(
                payload.ContainsKey(claim),
                $"External client {tokenKind} must not expose claim '{claim}'.");
        }
    }

    private static void AssertExternalTokenProfile(string jwt, string tokenKind)
    {
        Assert.IsLessThanOrEqualTo(
            MaxExternalAccessTokenLength,
            jwt.Length,
            $"External client {tokenKind} must stay within the token size profile.");
        var payload = ReadJwtPayload(jwt);
        Assert.IsLessThanOrEqualTo(
            MaxExternalAccessTokenClaimCount,
            payload.Count,
            $"External client {tokenKind} must not carry excessive claims.");
    }

    private static void AssertFirstPartyAccessTokenIncludesSecurityStamp(string accessToken)
    {
        var payload = ReadJwtPayload(accessToken);
        Assert.IsTrue(
            payload.ContainsKey(FullNetIdentityClaimTypes.SecurityStamp),
            "First-party access tokens must retain the security stamp for session authority.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            payload[FullNetIdentityClaimTypes.SecurityStamp]));
    }

    private static Dictionary<string, string?> ReadJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        Assert.IsTrue(parts.Length >= 2, "JWT must include a payload segment.");
        var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var document = JsonDocument.Parse(json);
        var payload = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            payload[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => property.Value.GetRawText(),
            };
        }

        return payload;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }
}