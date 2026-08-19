using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Organization.TenantUnits;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Organization;

[TestClass]
public sealed class OrganizationModuleRegistrationTests
{
    [TestMethod]
    public void Background_services_registers_identity_organization_unit_projection_source()
    {
        var services = new ServiceCollection();
        new OrganizationModule().AddBackgroundServices(
            services,
            new ConfigurationBuilder().Build());

        var sourceDescriptor = services.SingleOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IIdentityOrganizationUnitProjectionSource));
        Assert.IsNotNull(sourceDescriptor);
        Assert.AreEqual(ServiceLifetime.Scoped, sourceDescriptor!.Lifetime);

        var catalogDescriptor = services.SingleOrDefault(descriptor =>
            descriptor.ServiceType == typeof(OrganizationUnitProjectionCatalog));
        Assert.IsNotNull(catalogDescriptor);
        Assert.AreEqual(ServiceLifetime.Scoped, catalogDescriptor!.Lifetime);
    }
}
