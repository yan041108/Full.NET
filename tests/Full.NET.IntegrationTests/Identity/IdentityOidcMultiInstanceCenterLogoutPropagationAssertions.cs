using System.Net;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcMultiInstanceCenterLogoutPropagationAssertions
{
    private const string SigningKeyId = "logout-center-multi-key";

    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var dataProtectionAssets = IdentityOidcMultiInstanceTestSupport.CreateDataProtectionAssets();
        using var signingKey = System.Security.Cryptography.RSA.Create(3072);
        var settings = IdentityOidcMultiInstanceTestSupport.BuildFactorySettings(
            signingKey,
            SigningKeyId,
            dataProtectionAssets.KeyRingPath,
            dataProtectionAssets.CertificatePath);
        try
        {
            using var primaryFactory = new FullNetApiFactory(provider, connectionString, settings);
            using var secondaryFactory = primaryFactory.CreateIsolatedFactory();
            await primaryFactory.InitializeAsync(cancellationToken);
            await secondaryFactory.InitializeAsync(cancellationToken);

            using var primaryClient = primaryFactory.CreateClientForHost("localhost");
            using var secondaryClient = secondaryFactory.CreateClientForHost("localhost");
            await VerifyCenterLogoutRevokesAllRefreshOnSecondaryInstanceAsync(
                primaryClient,
                secondaryClient,
                cancellationToken);
        }
        finally
        {
            IdentityOidcMultiInstanceTestSupport.TryDeleteDirectory(dataProtectionAssets.RootPath);
        }
    }

    private static async Task VerifyCenterLogoutRevokesAllRefreshOnSecondaryInstanceAsync(
        HttpClient primaryClient,
        HttpClient secondaryClient,
        CancellationToken cancellationToken)
    {
        var publicFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            primaryClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        var confidentialFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            primaryClient,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(publicFlow.RefreshToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(confidentialFlow.RefreshToken));

        using var logoutResponse = await primaryClient.PostAsync(
            "/api/v1/identity/oidc/logout",
            null,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var publicRefreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            secondaryClient,
            publicFlow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(publicRefreshResult.IsSuccessStatusCode);

        var confidentialRefreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            secondaryClient,
            confidentialFlow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            cancellationToken);
        Assert.IsFalse(
            confidentialRefreshResult.IsSuccessStatusCode,
            $"Expected confidential client refresh to fail after center logout, got {(int)confidentialRefreshResult.StatusCode}: {confidentialRefreshResult.RawBody}");
    }
}