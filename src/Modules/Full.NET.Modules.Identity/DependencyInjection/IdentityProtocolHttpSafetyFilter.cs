using Full.NET.Modules.Identity.Oidc;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;

namespace Full.NET.Modules.Identity.DependencyInjection;

/// <summary>协议客户端的重试由凭据消费规则或持久化投递队列控制，禁止宿主 HTTP 策略额外重放。</summary>
internal sealed class IdentityProtocolHttpSafetyFilter : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        if (builder.Name != IdentityOidcLogoutDelivery.HttpClientName
            && builder.Name?.StartsWith("OpenIddict.Client.SystemNetHttp:", StringComparison.Ordinal) != true)
            return;

        // 待其他配置完成后只移除指定协议客户端的策略，普通业务客户端保持宿主韧性能力。
        for (var index = builder.AdditionalHandlers.Count - 1; index >= 0; index--)
        {
            if (builder.AdditionalHandlers[index] is not ResilienceHandler handler) continue;
            builder.AdditionalHandlers.RemoveAt(index);
            handler.Dispose();
        }
    };
}
