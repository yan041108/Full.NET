using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>数据库履约服务必须绑定当前请求事务，禁止从根容器捕获 Scoped 执行器。</summary>
[TestClass]
public sealed class TenantSubscriptionPaymentFulfillmentRegistrationTests
{
    [TestMethod]
    public void Payment_fulfillment_resolves_with_scope_validation_and_remains_scope_owned()
    {
        var moduleServices = new ServiceCollection();
        new TenancyModule().AddServices(moduleServices, new ConfigurationBuilder().Build());
        var descriptor = moduleServices.Single(service => service.ServiceType == typeof(ITenantSubscriptionPaymentFulfillmentPort));
        IServiceCollection services = new ServiceCollection();
        services.Add(descriptor);
        services.AddScoped(_ => Substitute.For<IQueryExecutor>());
        services.AddScoped(_ => Substitute.For<ICommandExecutor>());
        services.AddScoped(_ => Substitute.For<ICommandTransaction>());
        services.AddSingleton(Substitute.For<IClock>());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var firstPort = first.ServiceProvider.GetRequiredService<ITenantSubscriptionPaymentFulfillmentPort>();
        Assert.AreSame(firstPort, first.ServiceProvider.GetRequiredService<ITenantSubscriptionPaymentFulfillmentPort>());
        Assert.AreNotSame(firstPort, second.ServiceProvider.GetRequiredService<ITenantSubscriptionPaymentFulfillmentPort>());
    }
}
