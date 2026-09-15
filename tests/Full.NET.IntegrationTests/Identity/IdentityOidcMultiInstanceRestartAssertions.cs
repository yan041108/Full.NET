using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcMultiInstanceRestartAssertions
{
    public static async Task VerifyStaggeredExchangeAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var dataProtectionAssets = IdentityOidcMultiInstanceTestSupport.CreateDataProtectionAssets();
        using var signingKey = RSA.Create(3072);
        const string signingKeyId = "restart-probe-key";
        var settings = IdentityOidcMultiInstanceTestSupport.BuildFactorySettings(
            signingKey,
            signingKeyId,
            dataProtectionAssets.KeyRingPath,
            dataProtectionAssets.CertificatePath);
        try
        {
            using var primaryFactory = new FullNetApiFactory(provider, connectionString, settings);
            using var secondaryFactory = new FullNetApiFactory(provider, connectionString, settings);
            await primaryFactory.InitializeAsync(cancellationToken);
            await secondaryFactory.InitializeAsync(cancellationToken);
            using var primaryClient = primaryFactory.CreateClientForHost("localhost");
            using var secondaryClient = secondaryFactory.CreateClientForHost("localhost");
            var pending = await IdentityOidcRelyingPartyFixture.BeginAuthorizationCodeFlowAsync(
                primaryClient,
                IdentityOidcRelyingPartyFixture.PublicClientId,
                IdentityOidcRelyingPartyFixture.PublicRedirectUri,
                "admin",
                FullNetApiFactory.TestPassword,
                requestOfflineAccess: true,
                cancellationToken: cancellationToken);
            var flow = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(
                secondaryClient,
                pending.Code,
                pending.Verifier,
                pending.ClientId,
                pending.RedirectUri,
                null,
                expectedNonce: pending.Nonce,
                cancellationToken: cancellationToken);
            Assert.IsFalse(string.IsNullOrWhiteSpace(flow.AccessToken));
            Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

            await AssertMeAcceptsTokenAsync(secondaryClient, flow.AccessToken, cancellationToken);
            var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
                secondaryClient,
                flow.RefreshToken!,
                IdentityOidcRelyingPartyFixture.PublicClientId,
                null,
                cancellationToken);
            Assert.IsTrue(
                refreshResult.IsSuccessStatusCode,
                $"Expected refresh on peer instance after staggered exchange, got {(int)refreshResult.StatusCode}: {refreshResult.RawBody}");
        }
        finally
        {
            IdentityOidcMultiInstanceTestSupport.TryDeleteDirectory(dataProtectionAssets.RootPath);
        }
    }

    public static async Task VerifyPeerInstanceValidatesIssuedAccessTokenAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var dataProtectionAssets = IdentityOidcMultiInstanceTestSupport.CreateDataProtectionAssets();
        using var signingKey = RSA.Create(3072);
        const string signingKeyId = "restart-probe-key";
        var settings = IdentityOidcMultiInstanceTestSupport.BuildFactorySettings(
            signingKey,
            signingKeyId,
            dataProtectionAssets.KeyRingPath,
            dataProtectionAssets.CertificatePath);
        try
        {
            using var primaryFactory = new FullNetApiFactory(provider, connectionString, settings);
            using var secondaryFactory = new FullNetApiFactory(provider, connectionString, settings);
            await primaryFactory.InitializeAsync(cancellationToken);
            await secondaryFactory.InitializeAsync(cancellationToken);
            string accessToken;
            string refreshToken;
            using (var primaryClient = primaryFactory.CreateClientForHost("localhost"))
            {
                var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
                    primaryClient,
                    IdentityOidcRelyingPartyFixture.PublicClientId,
                    IdentityOidcRelyingPartyFixture.PublicRedirectUri,
                    null,
                    "admin",
                    FullNetApiFactory.TestPassword,
                    requestOfflineAccess: true,
                    cancellationToken: cancellationToken);
                accessToken = flow.AccessToken;
                refreshToken = flow.RefreshToken!;
            }

            using var secondaryClient = secondaryFactory.CreateClientForHost("localhost");
            await AssertMeAcceptsTokenAsync(secondaryClient, accessToken, cancellationToken);
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(refreshToken),
                "Peer validation probe requires an offline-access refresh token to validate shared encryption material.");
        }
        finally
        {
            IdentityOidcMultiInstanceTestSupport.TryDeleteDirectory(dataProtectionAssets.RootPath);
        }
    }

    private static async Task AssertMeAcceptsTokenAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
