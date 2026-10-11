using Full.NET.Modules.ImportExport;
using Full.NET.Modules.ImportExport.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>两个宿主入口必须应用完整导入配置，并保留启动校验。</summary>
[TestClass]
public sealed class ImportExportConfigurationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Module_registration_applies_all_configured_import_options(bool worker)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:ImportExport:MaxUploadBytes"] = "4096",
            ["FullNet:ImportExport:MaxPreviewRows"] = "11",
            ["FullNet:ImportExport:ExecutionEnabled"] = "true",
            ["FullNet:ImportExport:PollSeconds"] = "5",
            ["FullNet:ImportExport:BatchSize"] = "3",
            ["FullNet:ImportExport:LeaseSeconds"] = "30",
            ["FullNet:ImportExport:RunSynchronously"] = "true",
        }).Build();
        using var provider = Register(worker, configuration).BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ImportExportOptions>>().Value;
        Assert.AreEqual(4096L, options.MaxUploadBytes);
        Assert.AreEqual(11, options.MaxPreviewRows);
        Assert.IsTrue(options.ExecutionEnabled);
        Assert.AreEqual(5, options.PollSeconds);
        Assert.AreEqual(3, options.BatchSize);
        Assert.AreEqual(30, options.LeaseSeconds);
        Assert.IsTrue(options.RunSynchronously);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Module_registration_rejects_invalid_import_poll_budget(bool worker)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:ImportExport:ExecutionEnabled"] = "true",
            ["FullNet:ImportExport:PollSeconds"] = "4",
        }).Build();
        using var provider = Register(worker, configuration).BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<ImportExportOptions>>().Value;
        });
    }

    private static IServiceCollection Register(bool worker, IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        var module = new ImportExportModule();
        if (worker) module.AddBackgroundServices(services, configuration);
        else module.AddServices(services, configuration);
        return services;
    }
}
