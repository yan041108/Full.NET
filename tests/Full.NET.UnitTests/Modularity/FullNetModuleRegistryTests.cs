using Full.NET.Modularity.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Modularity;

[TestClass]
public sealed class FullNetModuleRegistryDescriptorTests
{
    [TestMethod]
    public void Descriptor_rejects_unknown_host_profiles()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FullNetModuleDescriptor.Create(
                "identity",
                "Identity",
                "1.0.0",
                [],
                ["Api", "Cloud"],
                FullNetModuleSourceClassification.Official,
                FullNetModuleHealthCapability.None));

        StringAssert.Contains(exception.Message, "Cloud", StringComparison.Ordinal);
    }

    [TestMethod]
    public void Descriptor_rejects_absolute_paths_in_display_name()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FullNetModuleDescriptor.Create(
                "identity",
                @"C:\src\Identity",
                "1.0.0",
                [],
                ["Api"],
                FullNetModuleSourceClassification.Official,
                FullNetModuleHealthCapability.None));

        StringAssert.Contains(exception.Message, "displayName", StringComparison.Ordinal);
    }

    [TestMethod]
    public void Descriptor_rejects_path_separators_in_version()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FullNetModuleDescriptor.Create(
                "identity",
                "Identity",
                "1.0.0/src",
                [],
                ["Api"],
                FullNetModuleSourceClassification.Official,
                FullNetModuleHealthCapability.None));

        StringAssert.Contains(exception.Message, "version", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Snapshot_rejects_duplicate_descriptor_keys()
    {
        var registry = new FullNetModuleRegistry();
        registry.Add(new NamedModule("base"));
        registry.Add(new NamedModule("dependent", ["base"]));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            FullNetModuleCatalogSnapshot.FromRegistry(
                registry,
                _ => FullNetModuleDescriptor.Create(
                    "base",
                    "Base",
                    "1.0.0",
                    [],
                    ["Api"],
                    FullNetModuleSourceClassification.Official,
                    FullNetModuleHealthCapability.None)));

        StringAssert.Contains(exception.Message, "base", StringComparison.Ordinal);
    }

    [TestMethod]
    public void Snapshot_lists_modules_in_dependency_order()
    {
        var registry = new FullNetModuleRegistry();
        registry.Add(new NamedModule("dependent", ["base"]));
        registry.Add(new NamedModule("base"));

        var snapshot = FullNetModuleCatalogSnapshot.FromRegistry(
            registry,
            module => FullNetModuleDescriptor.Create(
                module.Name,
                module.Name,
                "1.0.0",
                module.Dependencies,
                ["Api", "Worker", "Migrator"],
                FullNetModuleSourceClassification.Official,
                FullNetModuleHealthCapability.None));

        CollectionAssert.AreEqual(
            new[] { "base", "dependent" },
            snapshot.List().Select(item => item.ModuleKey).ToArray());
        Assert.IsNotNull(snapshot.FindByKey("dependent"));
        Assert.IsNull(snapshot.FindByKey("missing"));
    }

    private sealed class NamedModule(string name, IReadOnlyCollection<string>? dependencies = null)
        : IFullNetModule
    {
        public string Name { get; } = name;

        public IReadOnlyCollection<string> Dependencies { get; } = dependencies ?? [];

        public void AddServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }
    }
}
