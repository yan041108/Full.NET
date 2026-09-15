using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcJwksBoundaryAssertions
{
    private const string BoundaryKeyId = "fixture-oidc-jwks-boundary-key";
    private static readonly string[] ForbiddenPrivateJwkProperties =
    [
        "d",
        "p",
        "q",
        "dp",
        "dq",
        "qi",
        "oth",
        "k",
    ];

    private static readonly string[] ForbiddenPrivateMaterialMarkers =
    [
        "PrivateKeyPem",
        "privateKeyPem",
        "BEGIN RSA PRIVATE KEY",
        "BEGIN PRIVATE KEY",
        "BEGIN EC PRIVATE KEY",
    ];

    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        using var signingKey = RSA.Create(3072);
        using var factory = new FullNetApiFactory(
            provider,
            connectionString,
            BuildSettings(signingKey));
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyDiscoveryDoesNotExposePrivateMaterialAsync(client, cancellationToken);
        await VerifyJwksExposesOnlyPublicMaterialAsync(client, cancellationToken);
    }

    private static async Task VerifyDiscoveryDoesNotExposePrivateMaterialAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/.well-known/openid-configuration", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        AssertDoesNotExposePrivateMaterial(rawBody, "openid-configuration");
        using var document = JsonDocument.Parse(rawBody);
        Assert.IsTrue(document.RootElement.TryGetProperty("jwks_uri", out _));
    }

    private static async Task VerifyJwksExposesOnlyPublicMaterialAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/.well-known/jwks", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        AssertDoesNotExposePrivateMaterial(rawBody, "jwks");

        using var document = JsonDocument.Parse(rawBody);
        Assert.IsTrue(document.RootElement.TryGetProperty("keys", out var keys));
        Assert.IsTrue(keys.GetArrayLength() >= 1, "JWKS must publish at least one signing key.");

        var sawConfiguredKey = false;
        foreach (var key in keys.EnumerateArray())
        {
            foreach (var property in ForbiddenPrivateJwkProperties)
            {
                Assert.IsFalse(
                    key.TryGetProperty(property, out _),
                    $"JWKS key must not expose private JWK property '{property}'.");
            }

            Assert.IsTrue(key.TryGetProperty("kty", out var kty));
            Assert.AreEqual("RSA", kty.GetString());
            Assert.IsTrue(key.TryGetProperty("kid", out var kid));
            Assert.IsFalse(string.IsNullOrWhiteSpace(kid.GetString()));
            Assert.IsTrue(key.TryGetProperty("n", out _));
            Assert.IsTrue(key.TryGetProperty("e", out _));
            if (string.Equals(kid.GetString(), BoundaryKeyId, StringComparison.Ordinal))
            {
                sawConfiguredKey = true;
            }
        }

        Assert.IsTrue(sawConfiguredKey, "JWKS must include the configured active signing key.");
    }

    private static void AssertDoesNotExposePrivateMaterial(string rawBody, string endpoint)
    {
        foreach (var marker in ForbiddenPrivateMaterialMarkers)
        {
            Assert.IsFalse(
                rawBody.Contains(marker, StringComparison.OrdinalIgnoreCase),
                $"{endpoint} must not expose private key material ({marker}).");
        }
    }

    private static IReadOnlyDictionary<string, string?> BuildSettings(RSA key)
    {
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "false",
            ["Identity:Oidc:ActiveSigningKeyId"] = BoundaryKeyId,
            [$"Identity:Oidc:SigningKeys:{BoundaryKeyId}:PublicKeyPem"] = key.ExportRSAPublicKeyPem(),
            [$"Identity:Oidc:SigningKeys:{BoundaryKeyId}:PrivateKeyPem"] = key.ExportRSAPrivateKeyPem(),
        };
        return settings;
    }
}