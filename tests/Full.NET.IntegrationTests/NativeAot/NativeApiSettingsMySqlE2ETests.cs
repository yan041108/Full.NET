using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>MySQL 上的 Settings Native AOT 外部进程门禁。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiSettingsMySqlE2ETests
{
    [TestMethod]
    public async Task MySql_native_artifact_supports_settings_http_json()
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiSettingsE2EAssertions.VerifySettingsFlowAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }
}
