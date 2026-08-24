using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

[TestClass]
[DoNotParallelize]
public sealed class NativeApiS3MySqlE2ETests
{
    [TestMethod]
    public async Task MySql_native_artifact_uploads_downloads_and_deletes_via_s3_minio()
    {
        if (!NativeApiArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        }

        await NativeApiS3E2EAssertions.VerifyS3HttpFlowAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }
}
