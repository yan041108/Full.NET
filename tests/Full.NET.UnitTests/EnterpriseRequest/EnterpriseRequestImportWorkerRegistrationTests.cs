using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.ImportExport;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Reporting;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>独立 Worker 不调用 API 注册，也必须能加载真实导入处理器及组织授权边界。</summary>
[TestClass]
public sealed class EnterpriseRequestImportWorkerRegistrationTests
{
    [TestMethod]
    public void Worker_only_registration_resolves_both_schemas_and_binds_execution_options()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:ImportExport:ExecutionEnabled"] = "true",
            ["FullNet:ImportExport:BatchSize"] = "17",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<DatabaseOptions>();
        services.AddSingleton(Substitute.For<IQueryExecutor>());
        services.AddSingleton(Substitute.For<ICommandExecutor>());
        services.AddSingleton(Substitute.For<ICommandTransaction>());
        services.AddSingleton(Substitute.For<ICurrentTenant>());
        services.AddSingleton(Substitute.For<IBackgroundSessionAuthorization>());
        new OrganizationModule().AddBackgroundServices(services, configuration);
        new ReportingModule().AddBackgroundServices(services, configuration);
        new ImportExportModule().AddBackgroundServices(services, configuration);
        new EnterpriseRequestModule().AddBackgroundServices(services, configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<StaticImportSchemaRegistry>();
        var enterprise = registry.TryResolve(EnterpriseRequestImportSchemaKeys.EnterpriseRequests);
        Assert.IsNotNull(enterprise);
        Assert.IsNotNull(registry.TryResolve(StaticImportSchemaKeys.OrganizationTenantPositions));
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IOrganizationOwnedEntityWriteAuthorizer>());
        Assert.IsTrue(enterprise.CreateTemplate("requests").AsSpan().StartsWith("PK"u8));
        var options = provider.GetRequiredService<IOptions<ImportExportOptions>>().Value;
        Assert.IsTrue(options.ExecutionEnabled);
        Assert.AreEqual(17, options.BatchSize);
    }
}
