using Full.NET.Modularity.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Modularity;

[TestClass]
public sealed class ModuleRegistryTests
{
    [TestMethod]
    public void Registry_orders_dependencies_before_dependents()
    {
        var registry = new FullNetModuleRegistry();
        registry.Add(new DependentModule());
        registry.Add(new BaseModule());

        var ordered = registry.GetOrderedModules();

        CollectionAssert.AreEqual(
            new[] { typeof(BaseModule), typeof(DependentModule) },
            ordered.Select(module => module.GetType()).ToArray());
    }

    [TestMethod]
    public void Registry_rejects_dependency_cycles()
    {
        var registry = new FullNetModuleRegistry();
        registry.Add(new CycleModule());

        var exception = Assert.Throws<InvalidOperationException>(
            registry.GetOrderedModules);

        StringAssert.Contains(exception.Message, "cycle", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BaseModule : TestModule
    {
        public override string Name => "base";
    }

    private sealed class DependentModule : TestModule
    {
        public override string Name => "dependent";

        public override IReadOnlyCollection<Type> Dependencies => [typeof(BaseModule)];
    }

    private sealed class CycleModule : TestModule
    {
        public override string Name => "cycle";

        public override IReadOnlyCollection<Type> Dependencies => [typeof(CycleModule)];
    }

    private abstract class TestModule : IFullNetModule
    {
        public abstract string Name { get; }

        public virtual IReadOnlyCollection<Type> Dependencies => [];

        public void AddServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }
    }
}
