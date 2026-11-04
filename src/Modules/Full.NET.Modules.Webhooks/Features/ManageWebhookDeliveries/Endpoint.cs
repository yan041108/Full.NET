using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Webhooks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Webhooks.Features.ManageWebhookDeliveries;

/// <summary>本企业明确订阅的投递读取，独立于订阅创建及状态写权限。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/webhooks/subscriptions/{subscriptionId:guid}/deliveries")
            .WithTags("WebhookDeliveries")
            .RequireAuthorization(FullNetPermissionPolicies.For(WebhookPermissions.SubscriptionsRead), FullNetPermissionPolicies.For(WebhookPermissions.DeliveriesRead));
        group.MapGet("/", async (Guid subscriptionId, int? page, int? pageSize, string? statusKey, WebhookDeliveryHistoryQueryService queries, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await queries.ListAsync(subscriptionId, page ?? 1, pageSize ?? 20, statusKey, token).ConfigureAwait(false), context))
            .WithName("webhooksListSubscriptionDeliveries")
            .Produces<PagedResult<WebhookDeliveryHistoryResponse>>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        group.MapGet("/{deliveryId:guid}", async (Guid subscriptionId, Guid deliveryId, WebhookDeliveryHistoryQueryService queries, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await queries.GetAsync(subscriptionId, deliveryId, token).ConfigureAwait(false), context))
            .WithName("webhooksGetSubscriptionDelivery")
            .Produces<WebhookDeliveryHistoryResponse>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        group.MapGet("/{deliveryId:guid}/attempts", async (Guid subscriptionId, Guid deliveryId, int? page, int? pageSize, WebhookDeliveryHistoryQueryService queries, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await queries.ListAttemptsAsync(subscriptionId, deliveryId, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context))
            .WithName("webhooksListDeliveryAttempts")
            .Produces<PagedResult<WebhookDeliveryAttemptResponse>>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        group.MapPost("/{deliveryId:guid}/replays", async (Guid subscriptionId, Guid deliveryId, ReplayWebhookDeliveryRequest request, WebhookDeliveryReplayService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.QueueAsync(subscriptionId, deliveryId, request, token).ConfigureAwait(false), context))
            .WithName("webhooksReplayDelivery").Produces<WebhookDeliveryReplayReceipt>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503)
            .RequireAuthorization(FullNetPermissionPolicies.For(WebhookPermissions.DeliveriesReplay));
        group.MapGet("/{deliveryId:guid}/replays/{operationId:guid}", async (Guid subscriptionId, Guid deliveryId, Guid operationId, WebhookDeliveryReplayService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.ReadAsync(subscriptionId, deliveryId, operationId, token).ConfigureAwait(false), context))
            .WithName("webhooksGetDeliveryReplayReceipt").Produces<WebhookDeliveryReplayReceipt>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
    }
}
