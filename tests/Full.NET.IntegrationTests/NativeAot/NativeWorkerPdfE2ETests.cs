using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using System.Net.Http.Headers;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>Worker 原生 PDF 格式扫描和已结算代次恢复；身份和 HTTP 准备使用 JIT API。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerPdfE2ETests
{
    [TestMethod]
    public Task SqlServer_native_worker_recovers_settled_pdf_generation() => Verify(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_native_worker_recovers_settled_pdf_generation() => Verify(DatabaseProvider.MySql);
    private static async Task Verify(DatabaseProvider provider)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason)) Assert.Inconclusive(reason ?? "Native Worker artifact unavailable.");
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection, NativePdfKnowledgeAssertions.Settings(artifact.ExecutablePath));
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await NativePdfKnowledgeAssertions.VerifyAsync(client, token, provider, connection, default, nativeWorker: true);
    }
}
