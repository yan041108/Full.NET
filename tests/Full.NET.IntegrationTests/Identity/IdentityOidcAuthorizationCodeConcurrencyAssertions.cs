using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// HTTP 层同一授权码并发兑换：仅允许一次成功，其余返回 invalid_grant。
/// </summary>
internal static class IdentityOidcAuthorizationCodeConcurrencyAssertions
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

        var pending = await IdentityOidcRelyingPartyFixture.BeginAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);

        var attempts = Enumerable.Range(0, 8)
            .Select(_ => IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(
                client,
                pending.Code,
                pending.Verifier,
                pending.ClientId,
                pending.RedirectUri,
                null,
                expectedNonce: pending.Nonce,
                cancellationToken: cancellationToken))
            .ToArray();
        var results = await Task.WhenAll(attempts);
        var successCount = results.Count(result => !string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.AreEqual(1, successCount, "Exactly one concurrent authorization code exchange should succeed.");

        var failureBodies = results
            .Where(result => string.IsNullOrWhiteSpace(result.AccessToken))
            .Select(result => result.RawTokenResponse)
            .ToArray();
        Assert.IsTrue(failureBodies.Length >= 1, "Concurrent losers must be rejected.");
        Assert.IsTrue(
            failureBodies.All(body => body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)),
            "Rejected concurrent exchanges must return invalid_grant.");
    }
}