using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace Full.NET.Modules.Identity.DependencyInjection;

/// <summary>API 只发布可靠意图，Worker 只装配协议签名和投递，不引入 HTTP 认证服务器。</summary>
internal static class IdentityOidcLogoutServiceCollectionExtensions
{
    internal static void AddIdentityOidcLogout(this IServiceCollection services, IConfiguration configuration, bool worker)
    {
        if (!configuration.GetValue<bool>("Identity:Oidc:Enable")) return;
        services.TryAddScoped<IdentityOidcLogoutPublisher>();
        if (!services.Any(descriptor => descriptor.ImplementationInstance is IntegrationEventTopicDefinition topic
            && topic.TopicCode == "identity.oidc-logout.requested.v1"))
            services.AddSingleton(IntegrationEventTopicDefinition.Create("identity.oidc-logout.requested.v1",
                IdentityOidcLogoutRequested.EventType, 1, EventDeliveryOwner.LegacyPolling));
        if (!worker) return;
        services.AddOptions<IdentityOidcOptions>().Bind(configuration.GetSection(IdentityOidcOptions.SectionName));
        services.AddOptions<IdentityOidcOptions>().ValidateOnStart();
        services.AddOptions<IdentityOptions>().Bind(configuration.GetSection(IdentityOptions.SectionName));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Options.IValidateOptions<IdentityOidcOptions>, IdentityOidcOptionsValidator>());
        services.TryAddSingleton<IdentityOidcSigningKeyRing>();
        services.TryAddSingleton(new IdentityOidcSigningNodeIdentity("worker"));
        services.TryAddScoped<IdentityOidcKeyGovernance>();
        services.TryAddScoped<IdentityOidcLogoutDelivery>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventHandler, IdentityOidcLogoutRequestedHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, IdentityProtocolHttpSafetyFilter>());
        services.AddHttpClient(IdentityOidcLogoutDelivery.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                { AllowAutoRedirect = false, UseCookies = false, UseProxy = false });
        services.AddHostedService<IdentityOidcLogoutHostedService>();
    }
}
