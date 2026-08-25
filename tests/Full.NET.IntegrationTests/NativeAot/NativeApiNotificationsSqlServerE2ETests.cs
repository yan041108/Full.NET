using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

[TestClass]
[DoNotParallelize]
public sealed class NativeApiNotificationsSqlServerE2ETests
{
    [TestMethod]
    public async Task SqlServer_native_artifact_supports_notifications_http_json_signalr()
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiNotificationsE2EAssertions.VerifyNotificationsFlowAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }
}
