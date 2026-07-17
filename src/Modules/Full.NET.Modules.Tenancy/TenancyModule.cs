using FluentValidation;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.GetCurrentTenant;
using Full.NET.Modules.Tenancy.Features.ProvisionTenant;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Modules.Tenancy.Serialization;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Contracts;
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

    public IReadOnlyCollection<Type> Dependencies => [typeof(IdentityModule)];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            TenancyAuthorizationContributor>());
        services.AddOptions<TenancyOptions>()
            .Bind(configuration.GetSection(TenancyOptions.SectionName));
        services.AddFullNetFluentValidation();
        services.TryAddScoped<
            IValidator<ProvisionTenantCommand>,
            ProvisionTenantCommandValidator>();
        services.AddFullNetTenancyWorkerServices();
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();

        services.AddScoped<
            ICommandHandler<ProvisionTenantCommand, TenantSummary>,
            Features.ProvisionTenant.Handler>();
        services.AddScoped<
            IQueryHandler<GetCurrentTenantQuery, TenantSummary>,
            Features.GetCurrentTenant.Handler>();
        services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
        services.AddScoped<ITenantResolver, TenantResolver>();
        services.AddScoped<
            IQueryHandler<
                Features.GetAvailableTenants.Query,
                TenantContextSummary[]>,
            Features.GetAvailableTenants.Handler>();
        services.AddScoped<
            ICommandHandler<
                Features.ChangeTenantContext.Command,
                Full.NET.Modules.Identity.Contracts.TenantContextTokenResponse>,
            Features.ChangeTenantContext.Handler>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                TenancyJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenancy").WithTags("Tenancy");
        Features.GetCurrentTenant.Endpoint.Map(group);
        Features.GetAvailableTenants.Endpoint.Map(group);
        Features.ChangeTenantContext.Endpoint.Map(group);
    }
}
