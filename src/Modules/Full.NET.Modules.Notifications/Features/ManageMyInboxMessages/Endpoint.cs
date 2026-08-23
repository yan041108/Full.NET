using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.ManageMyInboxMessages;

internal static class Endpoint
{
    /// <summary>
    /// 注册当前用户收件箱路由组，包含列表、未读计数、单条标记已读与全部标记已读操作。
    /// </summary>
    /// <remarks>
    /// 收件箱按当前认证用户过滤，收件人标识来自可信 <c>sub</c> 声明而非请求体；
    /// 每个操作绑定独立稳定权限码并经 <c>RequireAuthorization</c> 强制校验，
    /// 已读状态变更后由 Outbox 修复未读数徽标。
    /// </remarks>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/my-inbox-messages")
            .WithTags("NotificationsMyInboxMessages");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            MyInboxQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await queries.ListAsync(
                    userId,
                    page ?? 1,
                    pageSize ?? 20,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsListMyInboxMessages")
        .Produces<PagedResult<InboxMessageResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(InboxPermissions.Read));

        group.MapGet("/unread-count", async (
            MyInboxQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await queries.GetUnreadCountAsync(userId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsGetMyInboxUnreadCount")
        .Produces<InboxUnreadCountResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(InboxPermissions.Read));

        group.MapPost("/{messageId:guid}/read", async (
            Guid messageId,
            MyInboxManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.MarkReadAsync(userId, messageId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsMarkMyInboxMessageRead")
        .Produces<InboxMessageResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(InboxPermissions.MarkRead));

        group.MapPost("/read-all", async (
            MyInboxManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.MarkAllReadAsync(userId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsMarkAllMyInboxMessagesRead")
        .Produces<InboxUnreadCountResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(InboxPermissions.MarkAllRead));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
