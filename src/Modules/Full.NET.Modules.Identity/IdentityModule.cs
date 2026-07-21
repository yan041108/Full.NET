using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.Bootstrap;
using Full.NET.Modules.Identity.Features.Login;
using Full.NET.Modules.Identity.Features.GetNavigation;
using Full.NET.Modules.Identity.Features.ChangeSessionContext;
using Full.NET.Modules.Identity.Features.ManageSuperAdministrators;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Identity.Serialization;
using Full.NET.Modules.Identity.Resources;
using Full.NET.Modules.Identity.Seeding;
using Full.NET.Hosting.Api;
using Full.NET.Localization;
using Full.NET.Seeding.Abstractions;
using Full.NET.Validation.FluentValidation;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Threading.RateLimiting;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity;

public sealed class IdentityModule : IFullNetModule
{
    /// <summary>
    /// 匿名会话轮换与退出共享的限流策略，避免攻击者绕过登录限流持续消耗密码学与数据库资源。
    /// </summary>
    internal const string SessionMutationRateLimitPolicy = "identity-session-mutation";

    /// <summary>
    /// 浏览器管理端使用的精确来源 CORS 策略名称。
    /// </summary>
    public const string BrowserCorsPolicy = "FullNET.Identity.BrowserClients";

    public string Name => "Identity";

    public IReadOnlyCollection<Type> Dependencies => [];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddFullNetLocalization();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            IdentityAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            IdentityErrorResourceSource>());
        services.TryAddSingleton(provider => AuthorizationCatalog.Create(
            provider.GetServices<IAuthorizationCatalogContributor>()));
        services.TryAddSingleton<PermissionClaimEvaluator>();
        services.TryAddScoped<IPermissionSnapshotReader, PermissionSnapshotReader>();
        services.TryAddScoped<
            IIdentitySessionContextService,
            IdentitySessionContextService>();
        services.TryAddSingleton<NavigationProjector>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationHandler,
            FullNetPermissionHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IHostedService,
            AuthorizationCatalogValidator>());
        services.AddOptions<IdentityOptions>()
            .Bind(configuration.GetSection(IdentityOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<IdentityOptions>,
            IdentityOptionsValidator>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<AccessSessionValidator>();
        services.TryAddScoped<FullNetJwtBearerEvents>();
        services.TryAddScoped<
            Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser>,
            Microsoft.AspNetCore.Identity.PasswordHasher<IdentityUser>>();
        services.TryAddScoped<IIdentityBootstrapService, IdentityBootstrapService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IDataSeedContributor,
            HostAdministratorSeedContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IDataSeedContributor,
            E2eHostViewerSeedContributor>());
        services.TryAddScoped<ISuperAdministratorService, SuperAdministratorService>();
        services.TryAddScoped<SuperAdministratorManagementService>();
        services.TryAddScoped<SuperAdministratorQueryService>();
        services.TryAddScoped<HostUserQueryService>();
        services.TryAddScoped<HostUserManagementService>();
        services.AddFullNetFluentValidation();
        services.TryAddScoped<IValidator<Command>, LoginCommandValidator>();
        services.TryAddScoped<IValidator<Features.UpdateLocale.Command>,
            Features.UpdateLocale.Validator>();
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
        services.TryAddScoped<
            Full.NET.Abstractions.Messaging.ICommandHandler<
                Features.UpdateLocale.Command,
                LocalePreferenceResponse>,
            Features.UpdateLocale.Handler>();
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
                    jwt.EventsType = typeof(FullNetJwtBearerEvents);
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
        services.Replace(ServiceDescriptor.Singleton<
            IAuthorizationPolicyProvider,
            FullNetPermissionPolicyProvider>());
        services.Replace(ServiceDescriptor.Singleton<
            IAuthorizationMiddlewareResultHandler,
            FullNetAuthorizationResultHandler>());
        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IOptions<IdentityOptions>>((cors, identityOptions) =>
            {
                var allowedOrigins = identityOptions.Value.AllowedOrigins
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                var policy = new CorsPolicyBuilder();
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                }

                cors.AddPolicy(BrowserCorsPolicy, policy.Build());
            });
        var identityRateLimits = configuration
            .GetSection(IdentityOptions.SectionName)
            .Get<IdentityOptions>() ?? new IdentityOptions();
        services.AddRateLimiter(rateLimiter =>
        {
            rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiter.OnRejected = async (context, _) =>
            {
                var mapper = context.HttpContext.RequestServices
                    .GetRequiredService<IApiResultMapper>();
                var problem = mapper.Map(
                    Result<object?>.Failure(new Error(
                        Code: IdentityErrorCodes.AuthenticationRateLimited,
                        Message: "Too many authentication session requests.",
                        Type: ErrorType.RateLimited)),
                    context.HttpContext);
                await problem.ExecuteAsync(context.HttpContext).ConfigureAwait(false);
            };
            rateLimiter.AddPolicy("identity-login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = identityRateLimits.LoginRateLimitPermitLimitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
            rateLimiter.AddPolicy(SessionMutationRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = identityRateLimits.SessionMutationRateLimitPermitLimitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
            rateLimiter.AddPolicy(
                "identity-super-administrator-write",
                httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                        ?? httpContext.Connection.RemoteIpAddress?.ToString()
                        ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
        });
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
        Features.UpdateLocale.Endpoint.Map(endpoints);
        Features.GetNavigation.Endpoint.Map(endpoints);
        Features.ManageSuperAdministrators.Endpoint.Map(endpoints);
        Features.ManageHostUsers.Endpoint.Map(endpoints);
    }
}
