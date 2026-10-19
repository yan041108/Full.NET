using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>集成测试：NativeApiNotificationsMySqlE2E。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiNotificationsMySqlE2ETests
{
    [TestMethod]
    public async Task MySql_native_artifact_supports_typed_outbox_notifications_flow()
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiNotificationsE2EAssertions.VerifyNotificationsFlowAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }
}
