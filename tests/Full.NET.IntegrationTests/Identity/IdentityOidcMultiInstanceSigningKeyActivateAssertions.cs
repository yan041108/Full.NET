using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcMultiInstanceSigningKeyActivateAssertions
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
            var factorySettings = IdentityOidcMultiInstanceTestSupport.BuildDualPrivateKeyFactorySettings(
                keyA,
                keyB,
                IdentityOidcSigningKeyRotationAssertions.KeyBId,
                dataProtectionAssets.KeyRingPath,
                dataProtectionAssets.CertificatePath);
            using var primaryFactory = new FullNetApiFactory(provider, connectionString, factorySettings);
            using var secondaryFactory = primaryFactory.CreateIsolatedFactory();
            await primaryFactory.InitializeAsync(cancellationToken);
            await secondaryFactory.InitializeAsync(cancellationToken);

            using var primaryClient = primaryFactory.CreateClientForHost("localhost");
            using var secondaryClient = secondaryFactory.CreateClientForHost("localhost");
            await VerifyRuntimeActivatePreservesCrossInstanceValidationAsync(
                primaryClient,
                secondaryClient,
                cancellationToken);
        }
        finally
        {
            IdentityOidcMultiInstanceTestSupport.TryDeleteDirectory(dataProtectionAssets.RootPath);
        }
    }

    private static async Task VerifyRuntimeActivatePreservesCrossInstanceValidationAsync(
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
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.AreEqual(
            IdentityOidcSigningKeyRotationAssertions.KeyBId,
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(legacyFlow.AccessToken, "kid"));

        var primaryAdminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            primaryClient,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        await ActivateSigningKeyAsync(
            primaryClient,
            primaryAdminToken,
            IdentityOidcSigningKeyRotationAssertions.KeyAId,
            cancellationToken);

        var primaryRotatedFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            primaryClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.AreEqual(
            IdentityOidcSigningKeyRotationAssertions.KeyAId,
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(primaryRotatedFlow.AccessToken, "kid"));

        using (var legacyMeOnSecondary = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me"))
        {
            legacyMeOnSecondary.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                legacyFlow.AccessToken);
            using var legacyMeResponse = await secondaryClient.SendAsync(legacyMeOnSecondary, cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, legacyMeResponse.StatusCode);
        }

        var secondaryBeforeActivateFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
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
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(
                secondaryBeforeActivateFlow.AccessToken,
                "kid"));

        var secondaryAdminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            secondaryClient,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        await ActivateSigningKeyAsync(
            secondaryClient,
            secondaryAdminToken,
            IdentityOidcSigningKeyRotationAssertions.KeyAId,
            cancellationToken);

        var secondaryRotatedFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            secondaryClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.AreEqual(
            IdentityOidcSigningKeyRotationAssertions.KeyAId,
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(secondaryRotatedFlow.AccessToken, "kid"));

        using var legacyMeOnPrimary = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        legacyMeOnPrimary.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            legacyFlow.AccessToken);
        using var legacyMeOnPrimaryResponse = await primaryClient.SendAsync(legacyMeOnPrimary, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, legacyMeOnPrimaryResponse.StatusCode);
    }

    private static async Task ActivateSigningKeyAsync(
        HttpClient client,
        string adminToken,
        string keyId,
        CancellationToken cancellationToken)
    {
        using var activateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/oidc-signing-keys/{keyId}/activate");
        activateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var activateResponse = await client.SendAsync(activateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, activateResponse.StatusCode);
        var activated = await activateResponse.Content.ReadFromJsonAsync<OidcSigningKeyListResponse>(cancellationToken);
        Assert.IsNotNull(activated);
        Assert.AreEqual(keyId, activated.ActiveSigningKeyId);
        Assert.IsTrue(activated.Keys.Single(key => key.KeyId == keyId).IsActive);
    }
}