using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcSigningKeyRetirementAssertions
{
    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        using var keyA = RSA.Create(3072);
        using var keyB = RSA.Create(3072);

        using var legacyFactory = new FullNetApiFactory(
            provider,
            connectionString,
            IdentityOidcSigningKeyRotationAssertions.BuildDualPrivateKeySettings(
                keyA,
                keyB,
                IdentityOidcSigningKeyRotationAssertions.KeyAId));
        await legacyFactory.InitializeAsync(cancellationToken);
        using var legacyClient = legacyFactory.CreateClientForHost("localhost");
        var legacyFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            legacyClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.AreEqual(
            IdentityOidcSigningKeyRotationAssertions.KeyAId,
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(legacyFlow.AccessToken, "kid"));

        using var retiredFactory = new FullNetApiFactory(
            provider,
            connectionString,
            BuildSinglePrivateKeySettings(keyB, IdentityOidcSigningKeyRotationAssertions.KeyBId));
        await retiredFactory.InitializeAsync(cancellationToken);
        using var retiredClient = retiredFactory.CreateClientForHost("localhost");

        var jwksKids = await ReadJwksKeyIdsAsync(retiredClient, cancellationToken);
        Assert.IsFalse(jwksKids.Contains(IdentityOidcSigningKeyRotationAssertions.KeyAId));
        Assert.IsTrue(jwksKids.Contains(IdentityOidcSigningKeyRotationAssertions.KeyBId));

        using var legacyMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        legacyMeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            legacyFlow.AccessToken);
        using var legacyMeResponse = await retiredClient.SendAsync(legacyMeRequest, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            legacyMeResponse.StatusCode,
            "Retired signing keys must fail closed on resource APIs.");

        var rotatedFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            retiredClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.AreEqual(
            IdentityOidcSigningKeyRotationAssertions.KeyBId,
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(rotatedFlow.AccessToken, "kid"));
    }

    internal static IReadOnlyDictionary<string, string?> BuildSinglePrivateKeySettings(
        RSA key,
        string keyId)
    {
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "false",
            ["Identity:Oidc:ActiveSigningKeyId"] = keyId,
            [$"Identity:Oidc:SigningKeys:{keyId}:PublicKeyPem"] = key.ExportRSAPublicKeyPem(),
            [$"Identity:Oidc:SigningKeys:{keyId}:PrivateKeyPem"] = key.ExportRSAPrivateKeyPem(),
        };
        return settings;
    }

    private static async Task<HashSet<string>> ReadJwksKeyIdsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/.well-known/jwks", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.IsTrue(document.RootElement.TryGetProperty("keys", out var keys));
        var kids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys.EnumerateArray())
        {
            if (key.TryGetProperty("kid", out var kid))
            {
                var value = kid.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    kids.Add(value);
                }
            }
        }

        return kids;
    }
}