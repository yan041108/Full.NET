using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>SQL Server 上的 Jobs Native AOT 外部进程门禁。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiJobsSqlServerE2ETests
{
    [TestMethod]
    public async Task SqlServer_native_artifact_supports_jobs_http_json()
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiJobsE2EAssertions.VerifyJobsFlowAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }
}
