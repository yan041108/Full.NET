using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Webhooks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/webhooks/subscriptions")
            .WithTags("Webhooks");

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
