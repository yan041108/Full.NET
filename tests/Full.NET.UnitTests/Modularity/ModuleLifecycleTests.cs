using Full.NET.Modularity.Modules;

namespace Full.NET.UnitTests.Modularity;

[TestClass]
public sealed class ModuleLifecycleTests
{
    [TestMethod]
    public void Module_contract_does_not_expose_an_unexecuted_initialization_hook()
    {
        var initializeMethod = typeof(IFullNetModule).GetMethod("InitializeAsync");

        Assert.IsNull(
            initializeMethod,
            "没有真实初始化消费者时，不得保留宿主不会执行的生命周期承诺。");
    }

    [TestMethod]
    public void Module_contract_uses_stable_string_keys_for_dependencies()
    {
        var nameProperty = typeof(IFullNetModule).GetProperty(nameof(IFullNetModule.Name));
        var dependenciesProperty = typeof(IFullNetModule).GetProperty(
            nameof(IFullNetModule.Dependencies));

        Assert.AreEqual(typeof(string), nameProperty?.PropertyType);
        Assert.AreEqual(
            typeof(IReadOnlyCollection<string>),
            dependenciesProperty?.PropertyType);
    }
}
