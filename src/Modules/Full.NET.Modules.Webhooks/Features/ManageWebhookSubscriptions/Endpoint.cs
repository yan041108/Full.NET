using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Webhooks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>WebhookSubscriptions管理 API 路由注册。</summary>
namespace Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/webhooks/subscriptions")
            .WithTags("Webhooks");

        group.MapPost("/{subscriptionId:guid}/status", async (Guid subscriptionId, SetWebhookSubscriptionStatusRequest request,
            WebhookSubscriptionManagementService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.SetStatusAsync(subscriptionId, request, token).ConfigureAwait(false), context))
            .WithName("webhooksSetSubscriptionStatus")
            .Produces<WebhookSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(FullNetPermissionPolicies.For(WebhookPermissions.SubscriptionsRead),
                FullNetPermissionPolicies.For(WebhookPermissions.SubscriptionsSetStatus));

        group.MapGet("/", async (
            WebhookSubscriptionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("webhooksListSubscriptions")
        .Produces<IReadOnlyList<WebhookSubscriptionResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(WebhookPermissions.SubscriptionsRead));

        group.MapPost("/", async (
            CreateWebhookSubscriptionRequest request,
            WebhookSubscriptionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("webhooksCreateSubscription")
        .Produces<WebhookSubscriptionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(WebhookPermissions.SubscriptionsManage));
    }
}
