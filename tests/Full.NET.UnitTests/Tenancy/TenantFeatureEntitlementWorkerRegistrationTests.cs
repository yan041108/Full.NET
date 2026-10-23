using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>审批恢复从 Worker 启动工作流时也必须解析真实的租户权益端口。</summary>
[TestClass]
public sealed class TenantFeatureEntitlementWorkerRegistrationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Api_and_worker_resolve_one_scope_owned_feature_entitlement_port(bool api)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var module = new TenancyModule();
        if (api) module.AddServices(services, configuration);
        else module.AddBackgroundServices(services, configuration);
        module.AddBackgroundServices(services, configuration);
        Assert.AreEqual(1, services.Count(item => item.ServiceType == typeof(ITenantFeatureEntitlementPort)));
        services.AddScoped(_ => Substitute.For<IQueryExecutor>());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var port = first.ServiceProvider.GetRequiredService<ITenantFeatureEntitlementPort>();
        Assert.AreEqual("Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.TenantFeatureEntitlementPort", port.GetType().FullName);
        Assert.AreSame(port, first.ServiceProvider.GetRequiredService<ITenantFeatureEntitlementPort>());
        Assert.AreNotSame(port, second.ServiceProvider.GetRequiredService<ITenantFeatureEntitlementPort>());
    }
}
