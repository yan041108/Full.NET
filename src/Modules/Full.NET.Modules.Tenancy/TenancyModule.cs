using Full.NET.Abstractions.Messaging;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ProvisionTenant;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Tenancy;

public sealed class TenancyModule : IFullNetModule
{
    public string Name => "Tenancy";

    public IReadOnlyCollection<Type> Dependencies => [];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<
            ICommandHandler<ProvisionTenantCommand, TenantSummary>,
            Handler>();
        services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
