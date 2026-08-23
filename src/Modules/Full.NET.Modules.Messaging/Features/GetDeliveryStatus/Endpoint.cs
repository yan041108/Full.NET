using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Messaging.Features.GetDeliveryStatus;

internal static class Endpoint
{
    /// <summary>
    /// 注册事件交付状态查询路由，返回 Outbox 积压摘要与各事件流交付所有者总览。
    /// </summary>
    /// <remarks>绑定 <c>events.read</c> 权限，供运维监控积压与切流状态。</remarks>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/messaging/delivery-status")
            .WithTags("Messaging");

        group.MapGet("/", async (
            DeliveryStatusQueryService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<DeliveryStatusResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.EventsRead));
    }
}
