using System.Threading.RateLimiting;
using Full.NET.Modules.Identity.Configuration;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>协议错误可能在认证中间件内直接返回，须在协议验证和拒绝审计之前限制入口。</summary>
internal sealed class IdentityOidcProtocolRateLimitHandler(IdentityOidcProtocolRequestLimiter limiter)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.ProcessRequestContext>()
            .UseSingletonHandler<IdentityOidcProtocolRateLimitHandler>()
            .SetOrder(OpenIddictServerHandlers.InferEndpointType.Descriptor.Order + 500)
            .SetType(OpenIddictServerHandlerType.Custom).Build();

    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.EndpointType is not (OpenIddictServerEndpointType.Authorization or OpenIddictServerEndpointType.Token
            or OpenIddictServerEndpointType.UserInfo or OpenIddictServerEndpointType.EndSession)) return;
        var http = context.Transaction.GetHttpRequest()?.HttpContext;
        if (http is null || limiter.TryAcquire(http)) return;
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.ContentType = "application/json;charset=utf-8";
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers.RetryAfter = "60";
        await http.Response.WriteAsync("{\"error\":\"temporarily_unavailable\"}", context.CancellationToken).ConfigureAwait(false);
        context.HandleRequest();
    }
}

/// <summary>协议专用预算；来源地址已由可信代理管线解析，不使用请求自报的客户端或租户分区。</summary>
internal sealed class IdentityOidcProtocolRequestLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> limiter;
    public IdentityOidcProtocolRequestLimiter(IOptions<IdentityOidcOptions> options)
    {
        var permits = options.Value.ProtocolRateLimitPermitLimitPerMinute;
        limiter = PartitionedRateLimiter.Create<HttpContext, string>(http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
            }));
    }

    internal bool TryAcquire(HttpContext http)
    {
        using var lease = limiter.AttemptAcquire(http);
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
