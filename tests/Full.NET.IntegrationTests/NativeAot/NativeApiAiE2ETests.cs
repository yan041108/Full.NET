using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>AI 模块在 Native API 产物上的独立闭包探针；不依赖 critical_http_flow 全长链路。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiAiE2ETests
{
    [TestMethod]
    public async Task SqlServer_native_artifact_exposes_ai_tools_and_mcp_metadata()
    {
        if (!NativeApiArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        }

        await NativeApiE2EAssertions.VerifyAiModuleNativeClosureFlowAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_native_artifact_exposes_ai_tools_and_mcp_metadata()
    {
        if (!NativeApiArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        }

        await NativeApiE2EAssertions.VerifyAiModuleNativeClosureFlowAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }
}