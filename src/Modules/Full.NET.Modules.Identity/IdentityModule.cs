using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.Bootstrap;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity;

public sealed class IdentityModule : IFullNetModule
{
    public string Name => "Identity";

    public IReadOnlyCollection<Type> Dependencies => [];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<IdentityOptions>()
            .Bind(configuration.GetSection(IdentityOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<IdentityOptions>,
            IdentityOptionsValidator>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<
            Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser>,
            Microsoft.AspNetCore.Identity.PasswordHasher<IdentityUser>>();
        services.TryAddScoped<IIdentityBootstrapService, IdentityBootstrapService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddAuthorization();
        services.AddRateLimiter(_ => { });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
