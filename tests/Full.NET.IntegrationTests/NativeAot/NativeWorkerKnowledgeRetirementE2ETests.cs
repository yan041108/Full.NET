using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实原生 Worker 清理 TXT/PDF 旧代次；JIT 准备原会话、结算和原子切换。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerKnowledgeRetirementE2ETests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task SqlServer_native_worker_retires_superseded_generation(bool pdf) => Verify(DatabaseProvider.SqlServer, pdf);
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task MySql_native_worker_retires_superseded_generation(bool pdf) => Verify(DatabaseProvider.MySql, pdf);
    private static async Task Verify(DatabaseProvider provider, bool pdf)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out _, out var reason)) Assert.Inconclusive(reason ?? "Native Worker artifact unavailable.");
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await AiKnowledgeRetiredGenerationAssertions.VerifyAsync(provider, connection, pdf, nativeWorker: true);
    }
}
