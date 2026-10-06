using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Ai;

/// <summary>可信部署配置连接公开接受条件与 Worker 执行能力。</summary>
[TestClass]
public sealed class AiKnowledgePdfRuntimeTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Explicit_runtime_binds_on_both_profiles(bool worker)
    {
        var path = Path.GetFullPath(Environment.ProcessPath!);
        var config = Config(path, path, "sha256:" + new string('a', 64));
        var services = new ServiceCollection(); services.AddSingleton<IConfiguration>(config);
        if (worker) new AiModule().AddBackgroundServices(services, config); else new AiModule().AddServices(services, config);
        using var provider = services.BuildServiceProvider();
        Assert.IsTrue(provider.GetRequiredService<IOptions<AiKnowledgePdfRuntimeOptions>>().Value.Enabled);
        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(AiKnowledgeIndexPdfActivationStore) && d.Lifetime == ServiceLifetime.Scoped));
        if (!worker) Assert.IsFalse(services.Any(d => d.ServiceType == typeof(AiKnowledgeIndexPdfPlanningService)));
        else
        {
            foreach (var type in new[] {typeof(AiKnowledgeIndexPdfPlanningService), typeof(AiKnowledgeIndexPdfEmbeddingService),
                typeof(AiKnowledgeIndexPdfManifestStore), typeof(AiKnowledgeIndexPdfVectorStore), typeof(AiKnowledgeIndexPdfPersistenceService)})
                Assert.IsTrue(services.Any(d => d.ServiceType == type && d.Lifetime == ServiceLifetime.Scoped), type.Name);
            var sandbox = provider.GetRequiredService<AiKnowledgePdfSandboxOptions>();
            Assert.AreEqual(path, sandbox.NodeExecutablePath); Assert.AreEqual(path, sandbox.CliScriptPath);
        }
    }

    [TestMethod] [DataRow("relative")] [DataRow("missing")] [DataRow("tag")] [DataRow("uppercase")]
    public void Enabled_worker_rejects_invalid_trusted_execution_config(string fault)
    {
        var path = Path.GetFullPath(Environment.ProcessPath!);
        var config = Config(fault == "relative" ? "node" : fault == "missing" ? path + ".absent" : path,
            path, fault == "tag" ? "pdf:latest" : "sha256:" + new string(fault == "uppercase" ? 'A' : 'a', 64));
        var services = new ServiceCollection(); services.AddSingleton<IConfiguration>(config);
        new AiModule().AddBackgroundServices(services, config); using var provider = services.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<AiKnowledgePdfRuntimeOptions>>().Value);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Pdf_scan_is_explicit_and_projects_trusted_format(bool mysql)
    {
        var statement = mysql ? AiKnowledgeIndexWorkerSql.ScanMySql : AiKnowledgeIndexWorkerSql.ScanSqlServer;
        Assert.IsTrue(statement.Text.Contains("version.ContentType AS ContentType", StringComparison.Ordinal));
        Assert.IsTrue(statement.Text.Contains("@PdfEnabled", StringComparison.Ordinal));
        Assert.IsTrue(statement.Text.Contains("'application/pdf'", StringComparison.Ordinal));
    }
    private static IConfiguration Config(string node, string cli, string image) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
    {
        [$"{AiKnowledgePdfRuntimeOptions.SectionName}:Enabled"] = "true",
        [$"{AiKnowledgePdfRuntimeOptions.SectionName}:NodeExecutablePath"] = node,
        [$"{AiKnowledgePdfRuntimeOptions.SectionName}:CliScriptPath"] = cli,
        [$"{AiKnowledgePdfRuntimeOptions.SectionName}:ImageId"] = image
    }).Build();
}
