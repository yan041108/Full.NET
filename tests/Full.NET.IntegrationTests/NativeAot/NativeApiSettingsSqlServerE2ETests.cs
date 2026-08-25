using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>SQL Server 上的 Settings Native AOT 外部进程门禁。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiSettingsSqlServerE2ETests
{
    [TestMethod]
    public async Task SqlServer_native_artifact_supports_settings_http_json()
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiSettingsE2EAssertions.VerifySettingsFlowAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }
}
