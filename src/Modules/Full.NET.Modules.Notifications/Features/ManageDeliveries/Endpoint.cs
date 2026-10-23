using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.ManageDeliveries;

internal static class Endpoint
{
    /// <summary>注册投递只读查询与人工重试；重试必须携带 CAS Revision 与短理由。</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/deliveries")
            .WithTags("NotificationsDeliveries");

        group.MapPost("/{deliveryId:guid}/reconcile", async (Guid deliveryId, ReconcileNotificationDeliveryRequest request,
            NotificationDeliveryService service, Execution.NotificationQuotaGovernor governor,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryResolveUserId(context, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.ReconcileAsync(actor, deliveryId, request, governor, token).ConfigureAwait(false), context);
        }).WithName("notificationsReconcileDelivery").Produces<NotificationDeliveryResponse>()
        .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.DeliveriesReconcile));

        group.MapGet("/summary", async (NotificationDeliveryService service, Providers.INotificationProviderTypeCatalog catalog,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.SummaryAsync(catalog, token).ConfigureAwait(false), context))
        .WithName("notificationsGetDeliverySummary").Produces<IReadOnlyList<NotificationDeliverySummaryResponse>>()
        .ProducesProblem(401).ProducesProblem(403)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.DeliveriesRead));

        group.MapPost("/{deliveryId:guid}/dead-letter", async (Guid deliveryId, RetryNotificationDeliveryRequest request,
            NotificationDeliveryService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryResolveUserId(context, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.DeadLetterAsync(actor, deliveryId, request, token).ConfigureAwait(false), context);
        }).WithName("notificationsDeadLetterDelivery").Produces<NotificationDeliveryResponse>()
        .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.DeliveriesDeadLetter));

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            NotificationDeliveryService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(page ?? 1, pageSize ?? 20, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsListDeliveries")
        .Produces<PagedResult<NotificationDeliveryResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.DeliveriesRead));

        group.MapGet("/{deliveryId:guid}", async (
            Guid deliveryId,
            NotificationDeliveryService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetByIdAsync(deliveryId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsGetDelivery")
        .Produces<NotificationDeliveryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.DeliveriesRead));

        group.MapPost("/{deliveryId:guid}/retry", async (
            Guid deliveryId,
            RetryNotificationDeliveryRequest request,
            NotificationDeliveryService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.RetryAsync(userId, deliveryId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsRetryDelivery")
        .Produces<NotificationDeliveryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.DeliveriesRetry));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
