using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Features.ManageMerchantConfigs;
using Full.NET.Modules.Payments.Features.ManageOrders;
using Full.NET.Modules.Payments.Features.ManageRefunds;
using Full.NET.Modules.Payments.Features.ReceiveWeChatNotify;
using Full.NET.Modules.Payments.Security;
using Full.NET.Modules.Payments.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Payments;

/// <summary>提供支付商户配置、微信 Native 支付订单、回调与退款 API。</summary>
public sealed class PaymentsModule : IFullNetModule
{
    public string Name => "Payments";

    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Tenancy",
    ];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            PaymentsAuthorizationContributor>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<PaymentSecretProtector>();
        services.AddHttpClient(WeChatNativePayClient.HttpClientName)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.mch.weixin.qq.com");
                client.Timeout = TimeSpan.FromSeconds(30);
            });
        services.TryAddSingleton<WeChatNativePayClient>();
        services.TryAddSingleton<IWeChatPayPlatformCertificateResolver, WeChatPayPlatformCertificateResolver>();
        services.TryAddScoped<PaymentMerchantConfigQueryService>();
        services.TryAddScoped<PaymentMerchantConfigManagementService>();
        services.TryAddScoped<PaymentOrderQueryService>();
        services.TryAddScoped<PaymentOrderManagementService>();
        services.TryAddScoped<PaymentOrderReconciliationService>();
        services.TryAddScoped<PaymentWeChatNotifyService>();
        services.TryAddScoped<PaymentRefundQueryService>();
        services.TryAddScoped<PaymentRefundManagementService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                PaymentsJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageMerchantConfigs.Endpoint.Map(endpoints);
        Features.ManageOrders.Endpoint.Map(endpoints);
        Features.ManageRefunds.Endpoint.Map(endpoints);
        Features.ReceiveWeChatNotify.Endpoint.Map(endpoints);
    }
}
