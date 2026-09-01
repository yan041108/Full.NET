using System.Security.Claims;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.ManageRecipientEndpoints;

/// <summary>注册当前用户收件端点的查询、登记和删除路由。</summary>
internal static class Endpoint
{
    /// <summary>映射只信任认证 Claim 的当前用户收件端点 API。</summary>
    /// <param name="endpoints">应用程序路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/my-recipient-endpoints")
            .WithTags("NotificationsRecipientEndpoints");

        group.MapGet("/", async (
            RecipientEndpointStore store,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await store.ListMineAsync(userId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsListMyRecipientEndpoints")
        .Produces<IReadOnlyList<RecipientEndpointResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            NotificationPlatformPermissions.PreferencesRead));

        group.MapPost("/", async (
            CreateMyRecipientEndpointRequest request,
            RecipientEndpointStore store,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await store.CreateMineAsync(userId, request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/notifications/my-recipient-endpoints/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("notificationsCreateMyRecipientEndpoint")
        .Produces<RecipientEndpointResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            NotificationPlatformPermissions.PreferencesUpdate));

        group.MapDelete("/{endpointId:guid}", async (
            Guid endpointId,
            RecipientEndpointStore store,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await store.DeleteMineAsync(userId, endpointId, cancellationToken)
                .ConfigureAwait(false);
            return result.IsSuccess
                ? Results.NoContent()
                : mapper.Map(result, httpContext);
        })
        .WithName("notificationsDeleteMyRecipientEndpoint")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            NotificationPlatformPermissions.PreferencesUpdate));
    }

    /// <summary>只从已验证身份中的 NameIdentifier 解析当前用户，拒绝请求体覆盖。</summary>
    /// <param name="httpContext">当前 HTTP 上下文。</param>
    /// <param name="userId">成功时返回当前用户标识。</param>
    /// <returns>Claim 存在且为非空 Guid 时返回 true。</returns>
    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        var value = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId) && userId != Guid.Empty;
    }
}
