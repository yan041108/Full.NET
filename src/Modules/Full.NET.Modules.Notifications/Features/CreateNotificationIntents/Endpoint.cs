using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.CreateNotificationIntents;

internal static class Endpoint
{
    /// <summary>
    /// 注册通知意图路由；幂等回放返回 200，首次受理返回 201。
    /// </summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/intents")
            .WithTags("NotificationsIntents");

        group.MapPost("/", async (
            CreateNotificationIntentRequest request,
            NotificationIntentService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(userId, request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            var created = result.Value!;
            if (created.Created)
            {
                return Results.Created(
                    $"/api/v1/notifications/intents/{created.Intent.Id:D}",
                    created.Intent);
            }

            return Results.Ok(created.Intent);
        })
        .WithName("notificationsCreateIntent")
        .Produces<NotificationIntentResponse>(StatusCodes.Status200OK)
        .Produces<NotificationIntentResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.IntentsCreate));

        group.MapGet("/{intentId:guid}", async (
            Guid intentId,
            NotificationIntentService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetByIdAsync(intentId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsGetIntent")
        .Produces<NotificationIntentResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.IntentsRead));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
