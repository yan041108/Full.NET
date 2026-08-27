using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>SQL Server 上的 Notifications Native AOT 外部进程门禁。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiNotificationsSqlServerE2ETests
{
    [TestMethod]
    public async Task SqlServer_native_artifact_supports_typed_outbox_notifications_flow()
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiNotificationsE2EAssertions.VerifyNotificationsFlowAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }
}
