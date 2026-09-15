using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcMultiInstanceSigningKeyRotationAssertions
{
    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var dataProtectionAssets = IdentityOidcMultiInstanceTestSupport.CreateDataProtectionAssets();
        using var keyA = RSA.Create(3072);
        using var keyB = RSA.Create(3072);
        try
        {
            using var primaryFactory = new FullNetApiFactory(
                provider,
                connectionString,
                IdentityOidcMultiInstanceTestSupport.BuildDualKeyFactorySettings(
                    keyA,
                    keyB,
                    IdentityOidcSigningKeyRotationAssertions.KeyAId,
                    dataProtectionAssets.KeyRingPath,
                    dataProtectionAssets.CertificatePath));
            using var secondaryFactory = new FullNetApiFactory(
                provider,
                connectionString,
                IdentityOidcMultiInstanceTestSupport.BuildDualKeyFactorySettings(
                    keyA,
                    keyB,
                    IdentityOidcSigningKeyRotationAssertions.KeyBId,
                    dataProtectionAssets.KeyRingPath,
                    dataProtectionAssets.CertificatePath));
            await primaryFactory.InitializeAsync(cancellationToken);
            await secondaryFactory.InitializeAsync(cancellationToken);

            using var primaryClient = primaryFactory.CreateClientForHost("localhost");
            using var secondaryClient = secondaryFactory.CreateClientForHost("localhost");
            await VerifyLegacyTokenValidOnRotatedSecondaryInstanceAsync(
                primaryClient,
                secondaryClient,
                cancellationToken);
        }
        finally
        {
            IdentityOidcMultiInstanceTestSupport.TryDeleteDirectory(dataProtectionAssets.RootPath);
        }
    }

    private static async Task VerifyLegacyTokenValidOnRotatedSecondaryInstanceAsync(
        HttpClient primaryClient,
        HttpClient secondaryClient,
        CancellationToken cancellationToken)
    {
        var legacyFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            primaryClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.AreEqual(
            IdentityOidcSigningKeyRotationAssertions.KeyAId,
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(legacyFlow.AccessToken, "kid"));

        using var legacyMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        legacyMeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            legacyFlow.AccessToken);
        using var legacyMeResponse = await secondaryClient.SendAsync(legacyMeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, legacyMeResponse.StatusCode);

        var jwksKids = await ReadJwksKeyIdsAsync(secondaryClient, cancellationToken);
        Assert.IsTrue(jwksKids.Contains(IdentityOidcSigningKeyRotationAssertions.KeyAId));
        Assert.IsTrue(jwksKids.Contains(IdentityOidcSigningKeyRotationAssertions.KeyBId));

        var rotatedFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            secondaryClient,
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

        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            secondaryClient,
            legacyFlow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsTrue(
            refreshResult.IsSuccessStatusCode,
            $"Expected legacy refresh to succeed on rotated secondary instance, got {(int)refreshResult.StatusCode}: {refreshResult.RawBody}");
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