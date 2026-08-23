using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;

internal static class Endpoint
{
    /// <summary>
    /// 注册事件交付所有权切换路由，将指定事件流从 Legacy 轮询切换到 CDC Kafka。
    /// </summary>
    /// <remarks>
    /// 绑定 <c>delivery.cutover</c> 权限；切流经 CAS 守卫与积压排空前置条件，
    /// 请求必须携带运维理由，切流与领域审计同事务写入。
    /// </remarks>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/messaging/delivery")
            .WithTags("Messaging");

        group.MapPost("/cutover", async (
            ChangeDeliveryOwnerRequest request,
            DeliveryCutoverService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CutoverAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<DeliveryCutoverResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.DeliveryCutover));
    }
}
