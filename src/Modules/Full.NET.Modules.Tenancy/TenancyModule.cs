using FluentValidation;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.GetCurrentTenant;
using Full.NET.Modules.Tenancy.Features.ProvisionTenant;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Modules.Tenancy.Resources;
using Full.NET.Modules.Tenancy.Seeding;
using Full.NET.Modules.Tenancy.Serialization;
using Full.NET.Seeding.Abstractions;
using Full.NET.Validation.FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Tenancy;

public sealed class TenancyModule : IFullNetModule
{
    public string Name => "Tenancy";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        AddMigrationServices(services, configuration);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            TenancyAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            TenancyErrorResourceSource>());
        AddBackgroundServices(services, configuration);
        services.AddScoped<
            IQueryHandler<GetCurrentTenantQuery, TenantSummary>,
            Features.GetCurrentTenant.Handler>();
        services.AddScoped<ITenantResolver, TenantResolver>();
        services.AddScoped<
            IQueryHandler<
                Features.GetAvailableTenants.Query,
                TenantContextSummary[]>,
            Features.GetAvailableTenants.Handler>();
        services.AddScoped<
            ICommandHandler<
                Features.ChangeTenantContext.Command,
                TenantContextTokenResponse>,
            Features.ChangeTenantContext.Handler>();
        services.AddScoped<Features.ManageHostTenants.HostTenantQueryService>();
        services.AddScoped<Features.ManageHostTenants.HostTenantManagementService>();
        services.AddScoped<Features.ManageHostTenantPackages.HostTenantPackageQueryService>();
        services.AddScoped<Features.ManageHostTenantPackages.HostTenantPackageManagementService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostDashboardTenantMetricsReader,
            HostDashboard.HostDashboardTenantMetricsReader>());
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                TenancyJsonSerializerContext.Default));
    }

    public void AddMigrationServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        AddTenantContextAccessor(services);
        services.AddOptions<TenancyOptions>()
            .Bind(configuration.GetSection(TenancyOptions.SectionName));
        services.AddFullNetFluentValidation();
        services.TryAddScoped<
            IValidator<ProvisionTenantCommand>,
            ProvisionTenantCommandValidator>();
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddScoped<
            ICommandHandler<ProvisionTenantCommand, TenantSummary>,
            Features.ProvisionTenant.Handler>();
        services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
        services.TryAddScoped<TenantCacheInvalidator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IDataSeedContributor,
            LocalTenantSeedContributor>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenancy").WithTags("Tenancy");
        Features.GetCurrentTenant.Endpoint.Map(group);
        Features.GetAvailableTenants.Endpoint.Map(group);
        Features.ChangeTenantContext.Endpoint.Map(group);
        Features.ManageHostTenants.Endpoint.Map(endpoints);
        Features.ManageHostTenantPackages.Endpoint.Map(endpoints);
    }

    /// <summary>
    /// 注册 Worker 消费租户事件所需的最小后台能力；不引入额外的模块拆分来承载唯一后台消费者。
    /// </summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        AddTenantContextAccessor(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            TenantProvisionedCacheInvalidationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            TenantChangedCacheInvalidationHandler>());
        services.TryAddScoped<TenantCacheInvalidator>();
    }

    /// <summary>
    /// Migrator Seed/开通与 Outbox 写入都依赖宿主租户上下文；必须在迁移闭包中注册，不能仅留在 Worker 后台能力里。
    /// </summary>
    private static void AddTenantContextAccessor(IServiceCollection services)
    {
        services.TryAddScoped<CurrentTenantAccessor>();
        services.TryAddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
    }

    /// <summary>
    /// 租户解析必须在认证之后、授权之前建立上下文，因此只在 <see cref="ModulePipelineStage.BeforeAuthorization"/> 注册。
    /// </summary>
    public void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage)
    {
        if (stage == ModulePipelineStage.BeforeAuthorization)
        {
            app.UseMiddleware<TenantResolutionMiddleware>();
        }
    }
}
