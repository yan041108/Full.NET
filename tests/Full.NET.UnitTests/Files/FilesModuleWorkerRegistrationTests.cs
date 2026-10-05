using Full.NET.Modules.Files;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Full.NET.Composition;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.UnitTests.Files;

/// <summary>Worker Profile must expose host file reference ports for Notifications background services.</summary>
[TestClass]
public sealed class FilesModuleWorkerRegistrationTests
{
    /// <summary>正式 Worker 必须使用真实配额确认，不能回退为总是成功的 Null 实现。</summary>
    [TestMethod]
    public void Worker_profile_uses_authoritative_tenant_storage_quota()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetApplicationModules(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Preset"] = "full",
        }).Build(), FullNetHostProfile.Worker, []);
        Assert.AreEqual(typeof(TenantFileStorageQuotaPort), services.Last(item => item.ServiceType == typeof(ITenantFileStorageQuotaPort)).ImplementationType);
        Assert.IsTrue(services.Any(item => item.ServiceType == typeof(ITenantQuotaReservationService)));
        Assert.IsTrue(services.Any(item => item.ServiceType == typeof(IIdentityActiveTenantDirectory) && item.Lifetime == ServiceLifetime.Scoped));
    }
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
