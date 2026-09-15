using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcGovernanceAssertions
{
    public static async Task VerifyAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        await VerifyV08WithoutOfflineAccessAsync(client, cancellationToken);
        AssertInconclusiveV09ConcurrentRefresh();
        AssertInconclusiveV11ScopePermissionBoundary();
        AssertInconclusiveV15CrossInstanceCache();
        AssertInconclusiveV19MigrationRecovery();
    }

    private static async Task VerifyV08WithoutOfflineAccessAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var result = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.IsNull(result.RefreshToken);
    }

    private static void AssertInconclusiveV09ConcurrentRefresh() =>
        Assert.Inconclusive("V09 concurrent refresh family reuse requires dedicated multi-flow harness.");

    private static void AssertInconclusiveV11ScopePermissionBoundary() =>
        Assert.Inconclusive("V11 scope and business permission boundary requires T05 resource API slice.");

    private static void AssertInconclusiveV15CrossInstanceCache() =>
        Assert.Inconclusive("V15 cross-instance client cache invalidation requires multi-instance harness.");

    private static void AssertInconclusiveV19MigrationRecovery() =>
        Assert.Inconclusive("V19 migration recovery is covered by Migration216IdentityOidcRecoveryTests.");
}