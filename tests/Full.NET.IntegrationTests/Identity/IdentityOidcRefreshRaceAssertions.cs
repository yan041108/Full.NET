using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// 验证 OIDC refresh token 在并发刷新下的族重用检测与失败关闭。
/// </summary>
internal static class IdentityOidcRefreshRaceAssertions
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

        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

        var firstTask = IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        var secondTask = IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        await Task.WhenAll(firstTask, secondTask);
        var firstResult = await firstTask;
        var secondResult = await secondTask;

        var successCount = new[] { firstResult, secondResult }.Count(result => result.IsSuccessStatusCode);
        var failureCount = new[] { firstResult, secondResult }.Count(result => !result.IsSuccessStatusCode);
        Assert.AreEqual(1, successCount, "Exactly one concurrent OIDC refresh should succeed.");
        Assert.AreEqual(1, failureCount, "Exactly one concurrent OIDC refresh should fail.");

        var failure = firstResult.IsSuccessStatusCode ? secondResult : firstResult;
        Assert.IsTrue(
            failure.RawBody.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
                || failure.RawBody.Contains("error", StringComparison.OrdinalIgnoreCase),
            $"Unexpected OIDC refresh failure body: {failure.RawBody}");

        var thirdResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(
            thirdResult.IsSuccessStatusCode,
            "Original refresh token must remain unusable after concurrent reuse detection.");
        StringAssert.Contains(
            thirdResult.RawBody,
            "invalid_grant",
            "Sequential reuse after concurrent race must reject the refresh token family.");
    }
}