using Full.NET.Composition;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Hosting;

/// <summary>直接编译模板组合根，验证三个宿主共享清单位置且不覆盖明确配置。</summary>
[TestClass]
public sealed class TemplateApplicationManifestTests
{
    [TestMethod]
    [DataRow(FullNetHostProfile.Api)]
    [DataRow(FullNetHostProfile.Worker)]
    [DataRow(FullNetHostProfile.Migrator)]
    public void Default_manifest_root_is_application_binary_directory(FullNetHostProfile profile)
    {
        using var provider = Build(profile, null);
        var options = provider.GetRequiredService<IOptions<FrameworkManifestMigrationOptions>>().Value;
        Assert.AreEqual(AppContext.BaseDirectory, options.ContentRoot);
        Assert.AreEqual("framework-manifest.json", options.RelativePath);
    }

    [TestMethod]
    [DataRow(FullNetHostProfile.Api)]
    [DataRow(FullNetHostProfile.Worker)]
    [DataRow(FullNetHostProfile.Migrator)]
    public void Explicit_manifest_root_is_preserved(FullNetHostProfile profile)
    {
        var root = Path.Combine(Path.GetTempPath(), "explicit-application-root");
        using var provider = Build(profile, root);
        Assert.AreEqual(root, provider.GetRequiredService<IOptions<FrameworkManifestMigrationOptions>>().Value.ContentRoot);
    }

    private static ServiceProvider Build(FullNetHostProfile profile, string? root)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Preset"] = "minimal",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<FrameworkManifestMigrationOptions>().Configure(options => options.ContentRoot = root);
        FullNetAppNameToken.Composition.ApplicationModuleCatalog.AddApplicationModules(services, configuration, profile);
        return services.BuildServiceProvider();
    }
}
