using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcSigningKeyRotationAssertions
{
    public const string KeyAId = "fixture-oidc-key-a";
    public const string KeyBId = "fixture-oidc-key-b";

    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        using var keyA = RSA.Create(3072);
        using var keyB = RSA.Create(3072);

        using var beforeFactory = new FullNetApiFactory(
            provider,
            connectionString,
            BuildDualKeySettings(keyA, keyB, KeyAId));
        await beforeFactory.InitializeAsync(cancellationToken);
        using var beforeClient = beforeFactory.CreateClientForHost("localhost");
        var legacyFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            beforeClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.AreEqual(
            KeyAId,
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(legacyFlow.AccessToken, "kid"));

        using var afterFactory = new FullNetApiFactory(
            provider,
            connectionString,
            BuildDualKeySettings(keyA, keyB, KeyBId));
        await afterFactory.InitializeAsync(cancellationToken);
        using var afterClient = afterFactory.CreateClientForHost("localhost");
        var jwksKids = await ReadJwksKeyIdsAsync(afterClient, cancellationToken);
        Assert.IsTrue(jwksKids.Contains(KeyAId));
        Assert.IsTrue(jwksKids.Contains(KeyBId));

        using var legacyMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        legacyMeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            legacyFlow.AccessToken);
        using var legacyMeResponse = await afterClient.SendAsync(legacyMeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, legacyMeResponse.StatusCode);

        var rotatedFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            afterClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.AreEqual(
            KeyBId,
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(rotatedFlow.AccessToken, "kid"));
    }

    internal static IReadOnlyDictionary<string, string?> BuildDualKeySettings(
        RSA keyA,
        RSA keyB,
        string activeKeyId)
    {
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "false",
            ["Identity:Oidc:ActiveSigningKeyId"] = activeKeyId,
            [$"Identity:Oidc:SigningKeys:{KeyAId}:PublicKeyPem"] = keyA.ExportRSAPublicKeyPem(),
            [$"Identity:Oidc:SigningKeys:{KeyBId}:PublicKeyPem"] = keyB.ExportRSAPublicKeyPem(),
        };
        if (string.Equals(activeKeyId, KeyAId, StringComparison.Ordinal))
        {
            settings[$"Identity:Oidc:SigningKeys:{KeyAId}:PrivateKeyPem"] = keyA.ExportRSAPrivateKeyPem();
        }
        else
        {
            settings[$"Identity:Oidc:SigningKeys:{KeyBId}:PrivateKeyPem"] = keyB.ExportRSAPrivateKeyPem();
        }

        return settings;
    }

    internal static IReadOnlyDictionary<string, string?> BuildDualPrivateKeySettings(
        RSA keyA,
        RSA keyB,
        string activeKeyId)
    {
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "false",
            ["Identity:Oidc:ActiveSigningKeyId"] = activeKeyId,
            ["Identity:Oidc:EncryptionKeyBase64"] =
                IdentityOidcMultiInstanceTestSupport.SharedEncryptionKeyBase64,
            [$"Identity:Oidc:SigningKeys:{KeyAId}:PublicKeyPem"] = keyA.ExportRSAPublicKeyPem(),
            [$"Identity:Oidc:SigningKeys:{KeyAId}:PrivateKeyPem"] = keyA.ExportRSAPrivateKeyPem(),
            [$"Identity:Oidc:SigningKeys:{KeyBId}:PublicKeyPem"] = keyB.ExportRSAPublicKeyPem(),
            [$"Identity:Oidc:SigningKeys:{KeyBId}:PrivateKeyPem"] = keyB.ExportRSAPrivateKeyPem(),
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