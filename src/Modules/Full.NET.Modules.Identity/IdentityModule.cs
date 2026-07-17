using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.Bootstrap;
using Full.NET.Modules.Identity.Features.Login;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Identity.Serialization;
using Full.NET.Validation.FluentValidation;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
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
        services.AddFullNetFluentValidation();
        services.TryAddScoped<IValidator<Command>, LoginCommandValidator>();
        services.TryAddScoped<
            Full.NET.Abstractions.Messaging.ICommandHandler<Command, LoginSessionResult>,
            Handler>();
        services.TryAddScoped<IdentityCookieWriter>();
        services.TryAddScoped<
            Full.NET.Abstractions.Messaging.ICommandHandler<
                Features.RefreshSession.Command,
                Features.RefreshSession.RefreshSessionResult>,
            Features.RefreshSession.Handler>();
        services.TryAddScoped<
            Full.NET.Abstractions.Messaging.ICommandHandler<
                Features.Logout.Command,
                Features.Logout.LogoutResult>,
            Features.Logout.Handler>();
        services.TryAddSingleton<RsaSigningKeyRing>();
        services.TryAddSingleton<IRandomTokenGenerator, CryptographicTokenGenerator>();
        services.TryAddSingleton<AllowedOriginValidator>();
        services.TryAddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => options.MapInboundClaims = false);
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<RsaSigningKeyRing, IOptions<IdentityOptions>>(
                (jwt, keyRing, identityOptions) =>
                {
                    var settings = identityOptions.Value;
                    jwt.MapInboundClaims = false;
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKeys = keyRing.ValidationKeys,
                        ValidateIssuer = true,
                        ValidIssuer = settings.Issuer,
                        ValidateAudience = true,
                        ValidAudience = settings.Audience,
                        ValidateLifetime = true,
                        RequireExpirationTime = true,
                        RequireSignedTokens = true,
                        ClockSkew = TimeSpan.FromSeconds(30),
                        NameClaimType = JwtRegisteredClaimNames.Name,
                    };
                });
        services.AddAuthorization();
        services.AddRateLimiter(rateLimiter =>
            rateLimiter.AddPolicy("identity-login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    })));
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                IdentityJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Identity");
        Features.Login.Endpoint.Map(group);
        Features.RefreshSession.Endpoint.Map(group);
        Features.Logout.Endpoint.Map(group);
        Features.GetCurrentUser.Endpoint.Map(endpoints);
    }
}
