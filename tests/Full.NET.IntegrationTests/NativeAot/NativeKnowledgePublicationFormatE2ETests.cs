using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>只覆盖 K01 改变的两类归档证明与历史闭包，不重跑整个 Native AI 业务套件。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeKnowledgePublicationFormatE2ETests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public Task SqlServer_native_archive_proof_and_history(bool docx) => Verify(DatabaseProvider.SqlServer, docx);
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public Task MySql_native_archive_proof_and_history(bool docx) => Verify(DatabaseProvider.MySql, docx);

    private static async Task Verify(DatabaseProvider provider, bool docx)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connection);
        await using var api = await NativeApiProcessHost.StartAsync(artifact, provider, connection, NativePdfKnowledgeAssertions.Settings(artifact.ExecutablePath), NativeAotTestTimeouts.ProcessStartup);
        using var client = api.CreateClient();
        var token = await NativeApiE2EAssertions.LoginAsync(client, api.LogFilePath);
        Console.WriteLine($"Native archive proof: provider={provider}, docx={docx}, log={api.LogFilePath}");
        await NativePdfKnowledgeAssertions.VerifyAsync(client, token, provider, connection, default, docx: docx);
        await api.StopGracefullyAsync(); api.AssertNoFatalMarkersInLogs(); Assert.AreEqual(0, api.ExitCode);
    }
}
