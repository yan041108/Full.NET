using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>MySQL 上的 Workflow Native AOT 外部进程门禁。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiWorkflowMySqlE2ETests
{
    [TestMethod]
    public async Task MySql_native_artifact_supports_workflow_approval_flow()
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiWorkflowE2EAssertions.VerifyWorkflowFlowAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }
}
