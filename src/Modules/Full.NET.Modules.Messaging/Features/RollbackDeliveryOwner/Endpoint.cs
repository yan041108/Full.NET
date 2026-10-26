using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Messaging.Features.RollbackDeliveryOwner;

internal static class Endpoint
{
    /// <summary>
    /// 注册事件交付所有权回退路由，将指定事件流从 CDC Kafka 回退到 Legacy 轮询。
    /// </summary>
    /// <remarks>
    /// 绑定 <c>delivery.rollback</c> 权限；回退经两阶段准备与控制面就绪证明，
    /// 请求必须携带运维理由，回退与领域审计同事务写入。
    /// </remarks>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/messaging/delivery")
            .WithTags("Messaging");

        group.MapGet("/rollback-operations", async (string eventType, int schemaVersion,
            DeliveryRollbackRecoveryService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.FindLatestAsync(eventType, schemaVersion, token).ConfigureAwait(false), context))
            .WithName("messagingGetLatestRollbackOperation")
            .Produces<RollbackOperationResponse>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.EventsRead));

        group.MapGet("/rollback-operations/{generation:guid}", async (Guid generation,
            DeliveryRollbackRecoveryService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.FindAsync(generation, token).ConfigureAwait(false), context))
            .WithName("messagingGetRollbackOperation")
            .Produces<RollbackOperationResponse>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.EventsRead));

        group.MapPost("/rollback-operations/{generation:guid}/recover", async (Guid generation, RecoverRollbackRequest request,
            DeliveryRollbackRecoveryService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.RecoverAsync(generation, request, token).ConfigureAwait(false), context))
            .WithName("messagingRecoverRollbackOperation")
            .Produces<RollbackOperationResponse>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.DeliveryRollback));

        group.MapPost("/rollback", async (
            ChangeDeliveryOwnerRequest request,
            DeliveryRollbackService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RollbackAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("messagingRollbackDeliveryOwner")
        .Produces<DeliveryRollbackResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.DeliveryRollback));
    }
}
