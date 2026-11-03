using Full.NET.Modules.Identity;
using Full.NET.Seeding.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityMigrationSeedRegistrationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Oidc_seed_follows_enablement_and_is_registered_once(bool enabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Identity:Oidc:Enable"] = enabled.ToString() }).Build();
        var services = new ServiceCollection(); var module = new IdentityModule();
        module.AddMigrationServices(services, configuration);
        module.AddMigrationServices(services, configuration);
        var seeds = services.Where(descriptor => descriptor.ServiceType == typeof(IDataSeedContributor)).ToArray();
        Assert.AreEqual(enabled ? 1 : 0, seeds.Count(descriptor => descriptor.ImplementationType?.Name == "IdentityOidcClientSeedContributor"));
        Assert.AreEqual(1, seeds.Count(descriptor => descriptor.ImplementationType?.Name == "HostAdministratorSeedContributor"));
        Assert.IsTrue(seeds.All(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped));
    }
}
