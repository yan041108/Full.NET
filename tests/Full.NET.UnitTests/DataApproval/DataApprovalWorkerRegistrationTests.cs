using Full.NET.Modules.DataApproval;
using Full.NET.Modules.DataApproval.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Full.NET.Composition;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Serialization.MemoryPack;
using Full.NET.Caching.Fusion;

namespace Full.NET.UnitTests.DataApproval;

/// <summary>单元测试（数据审批）：Data Approval Worker Registration。</summary>
[TestClass]
public sealed class DataApprovalWorkerRegistrationTests
{
    /// <summary>实际 Worker 组合必须能构造审批恢复消费者，不能等后台轮询才暴露缺失依赖。</summary>
    [TestMethod]
    public async Task Worker_composition_resolves_recovery_and_authoritative_entitlement_port()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Preset"] = "full",
            ["Database:Provider"] = "SqlServer",
            ["Database:ConnectionString"] = "Server=localhost;Database=unused;User Id=unused;Password=unused;TrustServerCertificate=True",
        });
        builder.Services.AddFullNetDapper(builder.Configuration, "Testing");
        builder.Services.AddDataProtection();
        builder.Services.AddFullNetMemoryPack();
        builder.Services.AddFullNetCaching(builder.Configuration, "Testing");
        builder.Services.AddFullNetApplicationModules(builder.Configuration, FullNetHostProfile.Worker, []);
        await using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<DataApprovalRequestRecoveryBatchProcessor>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<DataApprovalRequestApplicationRecoveryBatchProcessor>());
        Assert.IsInstanceOfType<Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.TenantFeatureEntitlementPort>(
            scope.ServiceProvider.GetRequiredService<ITenantFeatureEntitlementPort>());
    }

    [TestMethod]
    public void Background_registration_binds_and_validates_recovery_options()
    {
        using var configured = CreateProvider(new Dictionary<string, string?>
        {
            ["DataApproval:RequestRecoveryWorker:BatchSize"] = "7",
            ["DataApproval:ApplicationRecoveryWorker:BatchSize"] = "9",
        });
        Assert.AreEqual(7, configured.GetRequiredService<IOptions<DataApprovalRequestRecoveryWorkerOptions>>().Value.BatchSize);
        Assert.AreEqual(9, configured.GetRequiredService<IOptions<DataApprovalRequestApplicationRecoveryWorkerOptions>>().Value.BatchSize);

        using var invalid = CreateProvider(new Dictionary<string, string?>
        {
            ["DataApproval:RequestRecoveryWorker:BatchSize"] = "0",
        });
        Assert.ThrowsExactly<OptionsValidationException>(
            invalid.GetRequiredService<IStartupValidator>().Validate);
    }

    private static ServiceProvider CreateProvider(IReadOnlyDictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        services.AddSingleton<IConfiguration>(configuration);
        new DataApprovalModule().AddBackgroundServices(services, configuration);
        return services.BuildServiceProvider();
    }
}
