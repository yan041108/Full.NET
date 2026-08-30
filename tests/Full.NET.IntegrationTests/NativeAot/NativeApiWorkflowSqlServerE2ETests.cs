using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>SQL Server 上的 Workflow Native AOT 外部进程门禁。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiWorkflowSqlServerE2ETests
{
    [TestMethod]
    public async Task SqlServer_native_artifact_supports_workflow_approval_flow()
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiWorkflowE2EAssertions.VerifyWorkflowFlowAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }
}
