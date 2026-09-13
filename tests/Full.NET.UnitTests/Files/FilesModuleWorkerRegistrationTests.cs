using Full.NET.Modules.Files;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Files;

/// <summary>Worker Profile must expose host file reference ports for Notifications background services.</summary>
[TestClass]
public sealed class FilesModuleWorkerRegistrationTests
{
    [TestMethod]
    public void AddBackgroundServices_registers_host_file_reference_claim_ports()
    {
        var services = new ServiceCollection();
        new FilesModule().AddBackgroundServices(
            services,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Files:Local:RootPath"] = "worker-files",
                })
                .Build());

        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHostFileReferenceClaimService)
            && descriptor.ImplementationType == typeof(HostFileReferenceClaimService)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHostFileDescriptorReader)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
    }
}