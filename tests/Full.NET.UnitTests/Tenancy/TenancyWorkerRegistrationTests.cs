using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Files;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>后台导入与审批必须复用权威租户目录、权益和配额，不能因模块注册顺序绕过配额。</summary>
[TestClass]
public sealed class TenancyWorkerRegistrationTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Worker_ports_resolve_without_http_and_use_real_quota_for_both_module_orders(bool filesFirst)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddOptions();
        var configuration = new ConfigurationBuilder().Build();
        if (filesFirst) new FilesModule().AddBackgroundServices(services, configuration);
        new TenancyModule().AddBackgroundServices(services, configuration);
        if (!filesFirst) new FilesModule().AddBackgroundServices(services, configuration);
        services.AddScoped(_ => Substitute.For<IQueryExecutor>());
        services.AddScoped(_ => Substitute.For<ICommandExecutor>());
        services.AddScoped(_ => Substitute.For<ICommandTransaction>());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope(); using var second = provider.CreateScope();
        var active = first.ServiceProvider.GetRequiredService<IIdentityActiveTenantDirectory>();
        Assert.AreSame<object>(active, first.ServiceProvider.GetRequiredService<ITenantActivityReadPort>());
        Assert.AreNotSame(active, second.ServiceProvider.GetRequiredService<IIdentityActiveTenantDirectory>());
        Assert.IsNotNull(first.ServiceProvider.GetRequiredService<ITenantFeatureEntitlementPort>());
        Assert.IsNotNull(first.ServiceProvider.GetRequiredService<ITenantQuotaReservationService>());
        var quota = first.ServiceProvider.GetRequiredService<ITenantFileStorageQuotaPort>();
        Assert.AreEqual(1, services.Count(descriptor => descriptor.ServiceType == typeof(ITenantFileStorageQuotaPort)));
        StringAssert.StartsWith(quota.GetType().FullName!, "Full.NET.Modules.Tenancy.");
        Assert.IsFalse(services.Any(descriptor => descriptor.ServiceType == typeof(Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider)));
    }
}
