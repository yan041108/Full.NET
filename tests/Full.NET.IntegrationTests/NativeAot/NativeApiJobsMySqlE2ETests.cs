using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>MySQL 上的 Jobs Native AOT 外部进程门禁。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiJobsMySqlE2ETests
{
    [TestMethod]
    public async Task MySql_native_artifact_supports_jobs_http_json()
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiJobsE2EAssertions.VerifyJobsFlowAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }
}
