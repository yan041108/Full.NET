using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.ManageWeChatMiniProgramBindings;

/// <summary>微信小程序 OpenId 绑定与订阅授权 HTTP 端点。</summary>
internal static class Endpoint
{
    /// <summary>注册绑定查询、code 交换与订阅登记路由。</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/wechat-miniprogram/bindings")
            .WithTags("NotificationsWeChatMiniProgramBindings");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            WeChatMiniProgramBindingService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(page ?? 1, pageSize ?? 20, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsListWeChatMiniProgramBindings")
        .Produces<PagedResult<WeChatMiniProgramBindingResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            NotificationPlatformPermissions.WeChatMiniProgramBindingsRead));

        group.MapGet("/mine", async (
            WeChatMiniProgramBindingService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.ListMineAsync(userId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsListMyWeChatMiniProgramBindings")
        .Produces<IReadOnlyList<WeChatMiniProgramBindingResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            NotificationPlatformPermissions.WeChatMiniProgramBindingsBind));

        group.MapPost("/exchange", async (
            ExchangeWeChatMiniProgramBindingRequest request,
            WeChatMiniProgramBindingService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.ExchangeCodeAsync(userId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsExchangeWeChatMiniProgramBinding")
        .Produces<WeChatMiniProgramBindingResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            NotificationPlatformPermissions.WeChatMiniProgramBindingsBind));

        group.MapPost("/{appId}/subscriptions", async (
            string appId,
            RecordWeChatMiniProgramSubscriptionRequest request,
            WeChatMiniProgramBindingService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.RecordSubscriptionAsync(userId, appId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsRecordWeChatMiniProgramSubscription")
        .Produces<WeChatMiniProgramBindingResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            NotificationPlatformPermissions.WeChatMiniProgramBindingsRecordSubscription));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
