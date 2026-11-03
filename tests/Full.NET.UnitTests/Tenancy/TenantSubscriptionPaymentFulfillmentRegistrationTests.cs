using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.SubscriptionBilling;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
using Full.NET.Modules.Tenancy.Auditing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>数据库履约服务必须绑定当前请求事务，禁止从根容器捕获 Scoped 执行器。</summary>
[TestClass]
public sealed class TenantSubscriptionPaymentFulfillmentRegistrationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Payment_fulfillment_resolves_with_scope_validation_and_remains_scope_owned(bool worker)
    {
        var moduleServices = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        if (worker) new TenancyModule().AddBackgroundServices(moduleServices, configuration);
        else new TenancyModule().AddServices(moduleServices, configuration);
        var descriptor = moduleServices.Single(service => service.ServiceType == typeof(ITenantSubscriptionBillingPort));
        IServiceCollection services = new ServiceCollection();
        services.Add(descriptor);
        foreach (var type in new[] { typeof(TenantSubscriptionBillingService), typeof(TenantSubscriptionWriteBoundary), typeof(TenantHostPackageBinder) })
            services.Add(moduleServices.Single(service => service.ServiceType == type));
        services.AddScoped(_ => Substitute.For<IQueryExecutor>());
        services.AddScoped(_ => Substitute.For<ICommandExecutor>());
        services.AddScoped(_ => Substitute.For<ICommandTransaction>());
        services.AddSingleton(Substitute.For<IClock>());
        services.AddSingleton(Substitute.For<IIdGenerator>());
        services.AddSingleton(Options.Create(new DatabaseOptions()));
        services.AddScoped(_ => Substitute.For<ITransactionalDomainAuditWriter<TenancyDomainAuditWrite>>());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var firstPort = first.ServiceProvider.GetRequiredService<ITenantSubscriptionBillingPort>();
        Assert.AreSame(firstPort, first.ServiceProvider.GetRequiredService<ITenantSubscriptionBillingPort>());
        Assert.AreNotSame(firstPort, second.ServiceProvider.GetRequiredService<ITenantSubscriptionBillingPort>());
    }
}
