using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

[TestClass]
[DoNotParallelize]
public sealed class NativeApiKafkaReplayMySqlE2ETests
{
    [TestMethod]
    public async Task MySql_native_artifact_replays_kafka_range_via_http()
    {
        if (!NativeApiArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        }

        await NativeApiKafkaReplayE2EAssertions.VerifyKafkaReplayHttpFlowAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }
}
