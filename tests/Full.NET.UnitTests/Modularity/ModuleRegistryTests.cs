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
            new[] { "base", "dependent" },
            ordered.Select(module => module.Name).ToArray());
    }

    [TestMethod]
    public void Registry_order_is_stable_across_registration_order()
    {
        var first = new FullNetModuleRegistry();
        first.Add(new IndependentModule());
        first.Add(new DependentModule());
        first.Add(new BaseModule());
        var second = new FullNetModuleRegistry();
        second.Add(new BaseModule());
        second.Add(new IndependentModule());
        second.Add(new DependentModule());

        var firstOrder = first.GetOrderedModules().Select(module => module.Name).ToArray();
        var secondOrder = second.GetOrderedModules().Select(module => module.Name).ToArray();

        CollectionAssert.AreEqual(new[] { "base", "dependent", "independent" }, firstOrder);
        CollectionAssert.AreEqual(firstOrder, secondOrder);
    }

    [TestMethod]
    public void Registry_rejects_blank_module_keys()
    {
        var registry = new FullNetModuleRegistry();

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.Add(new NamedModule("   ")));

        StringAssert.Contains(exception.Message, "module key", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Registry_rejects_duplicate_module_keys_from_different_types()
    {
        var registry = new FullNetModuleRegistry();
        registry.Add(new BaseModule());

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.Add(new DuplicateBaseModule()));

        StringAssert.Contains(exception.Message, "base", StringComparison.Ordinal);
    }

    [TestMethod]
    public void Registry_rejects_unknown_dependency_keys()
    {
        var registry = new FullNetModuleRegistry();
        registry.Add(new NamedModule("consumer", ["missing"]));

        var exception = Assert.Throws<InvalidOperationException>(
            registry.GetOrderedModules);

        StringAssert.Contains(exception.Message, "consumer", StringComparison.Ordinal);
        StringAssert.Contains(exception.Message, "missing", StringComparison.Ordinal);
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

        public override IReadOnlyCollection<string> Dependencies => ["base"];
    }

    private sealed class IndependentModule : TestModule
    {
        public override string Name => "independent";
    }

    private sealed class DuplicateBaseModule : TestModule
    {
        public override string Name => "base";
    }

    private sealed class CycleModule : TestModule
    {
        public override string Name => "cycle";

        public override IReadOnlyCollection<string> Dependencies => ["cycle"];
    }

    private sealed class NamedModule(
        string name,
        IReadOnlyCollection<string>? dependencies = null) : TestModule
    {
        public override string Name => name;

        public override IReadOnlyCollection<string> Dependencies => dependencies ?? [];
    }

    private abstract class TestModule : IFullNetModule
    {
        public abstract string Name { get; }

        public virtual IReadOnlyCollection<string> Dependencies => [];

        public void AddServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }
    }
}
