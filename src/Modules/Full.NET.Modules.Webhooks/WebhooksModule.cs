using Full.NET.Abstractions.Messaging;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Webhooks.Delivery;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Full.NET.Modules.Webhooks.Security;
using Full.NET.Modules.Webhooks.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Webhooks;

/// <summary>Webhook 订阅与投递模块。</summary>
public sealed class WebhooksModule : IFullNetModule
{
    /// <summary>
    /// Webhooks 模块的唯一稳定标识，固定为 "Webhooks"。
    /// </summary>
    public string Name => "Webhooks";

    /// <summary>
    /// Webhooks 模块依赖 Identity 与 Tenancy 模块，需在其后加载。
    /// </summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity", "Tenancy"];

    /// <summary>
    /// Webhooks 模块可选消费 Workflow 模块的事件契约，未启用时仍可独立运行。
    /// </summary>
    public IReadOnlyCollection<string> OptionalContractDependencies => ["Workflow"];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            WebhooksAuthorizationContributor>());
        AddDeliveryServices(services, configuration);
        services.AddScoped<Features.ManageWebhookSubscriptions.WebhookSubscriptionQueryService>();
        services.AddScoped<Features.ManageWebhookSubscriptions.WebhookSubscriptionManagementService>();
        services.AddScoped<WebhookEventEnqueueService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            WorkflowInstanceCompletedWebhookHandler>());
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                WebhooksJsonSerializerContext.Default));
#if FULLNET_AOT_COMPILE
        new Persistence.WebhooksDapperAotMaterializerContributor()
            .RegisterMaterializers(new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    public void AddBackgroundServices(IServiceCollection services, IConfiguration configuration)
    {
        AddDeliveryServices(services, configuration);
#if FULLNET_AOT_COMPILE
        new Persistence.WebhooksDapperAotMaterializerContributor()
            .RegisterMaterializers(new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        services.AddHostedService<WebhookDeliveryHostedProcessor>();
    }

    private static void AddDeliveryServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WebhookDeliveryWorkerOptions>(
            configuration.GetSection(WebhookDeliveryWorkerOptions.SectionName));
        services.AddHttpClient(
            WebhookHttpClientNames.Delivery,
            client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.MaxResponseContentBufferSize = 64 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
            });
        services.AddScoped<WebhookSigningSecretProtector>();
        services.AddScoped<WebhookDeliveryBatchProcessor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        Features.ManageWebhookSubscriptions.Endpoint.Map(endpoints);
}
