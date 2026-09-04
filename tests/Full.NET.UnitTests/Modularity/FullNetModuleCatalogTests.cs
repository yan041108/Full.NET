using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Composition;
using Full.NET.Modularity.Modules;
using Full.NET.Modularity.Messaging;
using Full.NET.Modules.CodeGeneration;
using Full.NET.Modules.Document;
using Full.NET.Modules.Files;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Jobs;
using Full.NET.Modules.Messaging;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Features.Login;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Settings;
using Full.NET.Modules.SerialNumbers;
using Full.NET.Modules.Auditing;
using Full.NET.Modules.ObservabilityAdmin;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Tenancy;
using Full.NET.Seeding.Abstractions;
using Full.NET.Messaging.Abstractions;
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
    public void Modularity_core_registers_scoped_empty_subscription_catalog_for_partial_hosts()
    {
        var services = CreateServices();

        services.AddFullNetModularity();

        var descriptor = services.Single(item =>
            item.ServiceType == typeof(IntegrationEventSubscriptionCatalog));
        Assert.AreEqual(ServiceLifetime.Scoped, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
            });
        using var scope = provider.CreateScope();
        Assert.IsNotNull(
            scope.ServiceProvider.GetService<IntegrationEventSubscriptionCatalog>());
    }

    [TestMethod]
    public void Identity_background_registration_is_idempotent_and_keeps_one_official_topic()
    {
        var services = CreateServices();
        IdentityModule.RegisterOrganizationUnitChangedTopic(services);
        IdentityModule.RegisterOrganizationUnitChangedTopic(services);

        using var provider = services.BuildServiceProvider();
        var topics = provider.GetServices<IntegrationEventTopicDefinition>().ToArray();
        Assert.HasCount(1, topics);
        Assert.AreEqual("organization.unit-changed.v1", topics[0].TopicCode);
    }

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
                typeof(SettingsModule),
                typeof(JobsModule),
                typeof(MessagingModule),
                typeof(TenancyModule),
                typeof(OrganizationModule),
                typeof(NotificationsModule),
                typeof(ObservabilityAdminModule),
                typeof(SerialNumbersModule),
                typeof(WorkflowModule),
            },
            modules,
            string.Join(
                Environment.NewLine,
                modules.Select(module => module.FullName)));

        var catalog = provider.GetRequiredService<IFullNetModuleCatalog>();
        Assert.HasCount(14, catalog.List());
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
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(ICurrentTenantContextWriter)
            && descriptor.Lifetime == ServiceLifetime.Scoped));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.AreSame(
            scope.ServiceProvider.GetRequiredService<ICurrentTenant>(),
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>());
    }

    [TestMethod]
    public void Migrator_profile_registers_host_navigation_catalog_seed_closure()
    {
        var services = CreateServices();

        services.AddFullNetApplicationModules(
            CreateConfiguration(),
            FullNetHostProfile.Migrator);

        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ImplementationType == typeof(HostNavigationCatalogSyncService)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(AuthorizationCatalog)
            && descriptor.Lifetime == ServiceLifetime.Singleton));
    }

    [TestMethod]
    public void Minimal_preset_registers_only_declared_modules_in_api_profile()
    {
        var services = CreateServices();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Preset"] = FullNetModuleSelectionOptions.Presets.Minimal,
        });

        services.AddFullNetApplicationModules(configuration, FullNetHostProfile.Api);

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IFullNetModuleCatalog>();
        Assert.HasCount(FullNetModuleSelection.MinimalPresetModuleNames.Count, catalog.List());
        Assert.IsNull(catalog.FindByKey("Document"));
        Assert.IsNotNull(catalog.FindByKey("Organization"));
    }

    private static ServiceCollection CreateServices() => new();

    private static IConfiguration CreateConfiguration(
        IReadOnlyDictionary<string, string?>? values = null)
    {
        var builder = new ConfigurationBuilder();
        if (values is not null)
        {
            builder.AddInMemoryCollection(values);
        }

        return builder.Build();
    }
}
