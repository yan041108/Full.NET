using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Composition;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.CodeGeneration;
using Full.NET.Modules.Document;
using Full.NET.Modules.Files;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Jobs;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Features.Login;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Settings;
using Full.NET.Modules.SerialNumbers;
using Full.NET.Modules.Auditing;
using Full.NET.Modules.Tenancy;
using Full.NET.Seeding.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Modularity;

[TestClass]
public sealed class FullNetModuleCatalogTests
{
    [TestMethod]
    public void Api_profile_registers_complete_modules_in_dependency_order()
    {
        var services = CreateServices();

        services.AddFullNetApplicationModules(CreateConfiguration(), FullNetHostProfile.Api);

        using var provider = services.BuildServiceProvider();
        var modules = provider.GetRequiredService<FullNetModuleRegistry>()
            .GetOrderedModules()
            .Select(module => module.GetType())
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                typeof(IdentityModule),
                typeof(AuditingModule),
                typeof(CodeGenerationModule),
                typeof(FilesModule),
                typeof(DocumentModule),
                typeof(JobsModule),
                typeof(NotificationsModule),
                typeof(TenancyModule),
                typeof(OrganizationModule),
                typeof(SerialNumbersModule),
                typeof(SettingsModule),
            },
            modules,
            string.Join(
                Environment.NewLine,
                modules.Select(module => module.FullName)));

        var catalog = provider.GetRequiredService<IFullNetModuleCatalog>();
        Assert.HasCount(11, catalog.List());
        Assert.IsNotNull(catalog.FindByKey("Identity"));
        Assert.AreEqual(
            FullNetModuleSourceClassification.Official,
            catalog.FindByKey("Identity")!.SourceClassification);
        Assert.IsNotNull(catalog.FindByKey("Document"));
    }

    [TestMethod]
    public void Worker_profile_registers_only_declared_background_capabilities()
    {
        var services = CreateServices();

        services.AddFullNetApplicationModules(
            CreateConfiguration(),
            FullNetHostProfile.Worker);

        Assert.IsFalse(services.Any(descriptor =>
            descriptor.ServiceType == typeof(FullNetModuleRegistry)));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IIntegrationEventHandler)));
    }

    [TestMethod]
    public void Migrator_profile_registers_seed_contributors_without_http_and_auth_runtime_services()
    {
        var services = CreateServices();

        services.AddFullNetApplicationModules(
            CreateConfiguration(),
            FullNetHostProfile.Migrator);

        var contributorDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IDataSeedContributor))
            .ToArray();
        Assert.IsTrue(
            contributorDescriptors.Length >= 2,
            "Migrator 必须保留 Identity/Tenancy 的 Seed Contributor 注册。");
        Assert.IsTrue(
            contributorDescriptors.All(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsFalse(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IAuthenticationSchemeProvider)));
        Assert.IsFalse(services.Any(descriptor =>
            descriptor.ServiceType == typeof(ICorsService)));
        Assert.IsFalse(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IConfigureOptions<RateLimiterOptions>)));
        Assert.IsFalse(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IConfigureOptions<JsonOptions>)));
        Assert.IsFalse(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IAuthorizationPolicyProvider)
            && descriptor.ImplementationType == typeof(Full.NET.Modules.Identity.Authorization.FullNetPermissionPolicyProvider)));
        Assert.IsFalse(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IIntegrationEventHandler)));
        Assert.IsFalse(services.Any(descriptor =>
            descriptor.ServiceType == typeof(Full.NET.Abstractions.Messaging.ICommandHandler<Command, LoginSessionResult>)));
    }

    [TestMethod]
    public void Migrator_profile_registers_tenant_context_for_seed_and_outbox()
    {
        var services = CreateServices();

        services.AddFullNetApplicationModules(
            CreateConfiguration(),
            FullNetHostProfile.Migrator);

        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(ICurrentTenant)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(CurrentTenantAccessor)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
    }

    private static ServiceCollection CreateServices() => new();

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder().Build();
}
