using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>SQL Server 上的 OIDC Native AOT 外部进程门禁。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiOidcSqlServerE2ETests
{
    [TestMethod]
    public async Task SqlServer_native_artifact_runs_oidc_protocol_flow()
    {
        if (!NativeApiArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        }

        await NativeApiOidcE2EAssertions.VerifyOidcProtocolFlowAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_native_artifact_validates_oidc_consumer_entry_paths()
    {
        if (!NativeApiArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        }

        await NativeApiOidcE2EAssertions.VerifyConsumerEntryMinimalPathsAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_native_artifact_supports_dual_instance_oidc_exchange()
    {
        if (!NativeApiArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        }

        await NativeApiOidcE2EAssertions.VerifyDualInstanceAuthorizationCodeExchangeAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }
}