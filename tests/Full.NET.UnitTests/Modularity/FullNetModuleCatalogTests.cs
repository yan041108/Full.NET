using Full.NET.Abstractions.Messaging;
using Full.NET.Composition;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Modularity;

[TestClass]
public sealed class FullNetModuleCatalogTests
{
    [TestMethod]
    [DataRow(FullNetHostProfile.Api)]
    [DataRow(FullNetHostProfile.Migrator)]
    public void Full_profiles_register_complete_modules_in_dependency_order(
        FullNetHostProfile profile)
    {
        var services = CreateServices();

        services.AddFullNetApplicationModules(CreateConfiguration(), profile);

        using var provider = services.BuildServiceProvider();
        var modules = provider.GetRequiredService<FullNetModuleRegistry>()
            .GetOrderedModules()
            .Select(module => module.GetType())
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { typeof(IdentityModule), typeof(TenancyModule) },
            modules);
    }

    [TestMethod]
    public void Worker_profile_registers_only_declared_background_capabilities()
    {
        var services = CreateServices();

        services.AddFullNetApplicationModules(
            CreateConfiguration(),
            FullNetHostProfile.Worker);

        Assert.IsFalse(services.Any(descriptor =>
            descriptor.ServiceType == typeof(FullNetModuleRegistry)));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IIntegrationEventHandler)));
    }

    private static ServiceCollection CreateServices() => new();

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder().Build();
}
