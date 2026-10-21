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

    /// <summary>
    /// 注册 Webhooks 模块的应用服务：订阅查询与管理、事件入队、Workflow 完成事件处理器、
    /// 授权目录、投递 HTTP 客户端与签名保护等，以及 AOT 物化器。
    /// </summary>
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

    /// <summary>
    /// 注册 Webhooks 后台投递能力：投递工作选项、签名保护、批量处理器与 Webhook 投递 Hosted 后台服务。
    /// </summary>
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
            .ConfigurePrimaryHttpMessageHandler(() => CreateDeliveryHandler());
        services.AddScoped<WebhookSigningSecretProtector>();
        services.AddScoped<WebhookDeliveryBatchProcessor>();
    }

    private static SocketsHttpHandler CreateDeliveryHandler()
    {
        var connector = new WebhookNetworkConnector(System.Net.Dns.GetHostAddressesAsync,
            WebhookNetworkConnector.ConnectSocketAsync);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = (context, cancellation) => connector.ConnectAsync(
                context.InitialRequestMessage.RequestUri!, context.DnsEndPoint, cancellation),
        };
    }

    /// <summary>
    /// 映射 Webhooks 模块的 HTTP API 端点：Webhook 订阅的查询与管理接口。
    /// </summary>
    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        Features.ManageWebhookSubscriptions.Endpoint.Map(endpoints);
}
