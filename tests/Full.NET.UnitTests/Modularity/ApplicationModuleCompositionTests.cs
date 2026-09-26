using Full.NET.Composition;
using Full.NET.Modularity.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Modularity;

[TestClass]
public sealed class ApplicationModuleCompositionTests
{
    [TestMethod]
    [DataRow(FullNetHostProfile.Api)]
    [DataRow(FullNetHostProfile.Worker)]
    [DataRow(FullNetHostProfile.Migrator)]
    public void Application_modules_use_dependency_order_and_only_the_requested_host_role(FullNetHostProfile profile)
    {
        var calls = new List<string>();
        var first = new ProbeModule("Catalog", ["Identity"], calls);
        var second = new ProbeModule("Orders", ["Catalog"], calls);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetApplicationModules(Configuration(), profile, [second, first]);
        CollectionAssert.AreEqual(new[] { $"Catalog:{profile}", $"Orders:{profile}" }, calls);
        if (profile == FullNetHostProfile.Api)
        {
            using var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<FullNetModuleRegistry>().GetOrderedModules();
            Assert.IsTrue(registry.Contains(first));
            var catalog = provider.GetRequiredService<IFullNetModuleCatalog>();
            Assert.AreEqual((FullNetModuleSourceClassification)3, catalog.FindByKey("Catalog")!.SourceClassification);
            Assert.AreEqual(FullNetModuleSourceClassification.Official, catalog.FindByKey("Identity")!.SourceClassification);
        }
    }

    [TestMethod]
    [DataRow(FullNetHostProfile.Api, "duplicate")]
    [DataRow(FullNetHostProfile.Worker, "duplicate")]
    [DataRow(FullNetHostProfile.Migrator, "duplicate")]
    [DataRow(FullNetHostProfile.Api, "missing")]
    [DataRow(FullNetHostProfile.Worker, "missing")]
    [DataRow(FullNetHostProfile.Migrator, "missing")]
    [DataRow(FullNetHostProfile.Api, "cycle")]
    [DataRow(FullNetHostProfile.Worker, "cycle")]
    [DataRow(FullNetHostProfile.Migrator, "cycle")]
    [DataRow(FullNetHostProfile.Api, "official")]
    [DataRow(FullNetHostProfile.Worker, "official")]
    [DataRow(FullNetHostProfile.Migrator, "official")]
    public void Invalid_application_graph_is_rejected_before_service_registration(FullNetHostProfile profile, string kind)
    {
        var calls = new List<string>();
        IFullNetModule[] modules = kind switch
        {
            "duplicate" => [new ProbeModule("Catalog", [], calls), new ProbeModule("Catalog", [], calls)],
            "missing" => [new ProbeModule("Catalog", ["Workflow"], calls)],
            "cycle" => [new ProbeModule("Catalog", ["Orders"], calls), new ProbeModule("Orders", ["Catalog"], calls)],
            _ => [new ProbeModule("Identity", [], calls)],
        };
        var services = new ServiceCollection();
        services.AddLogging();
        var before = services.ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddFullNetApplicationModules(Configuration(), profile, modules));
        Assert.AreEqual(0, calls.Count);
        CollectionAssert.AreEqual(before, services.ToArray());
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["FullNet:Modules:Preset"] = "minimal" }).Build();

    private sealed class ProbeModule(string name, IReadOnlyCollection<string> dependencies, List<string> calls) : IFullNetModule
    {
        public string Name => name;
        public IReadOnlyCollection<string> Dependencies => dependencies;
        public void AddServices(IServiceCollection services, IConfiguration configuration) => calls.Add($"{Name}:Api");
        public void AddBackgroundServices(IServiceCollection services, IConfiguration configuration) => calls.Add($"{Name}:Worker");
        public void AddMigrationServices(IServiceCollection services, IConfiguration configuration) => calls.Add($"{Name}:Migrator");
        public void MapEndpoints(IEndpointRouteBuilder endpoints) { }
    }
}
