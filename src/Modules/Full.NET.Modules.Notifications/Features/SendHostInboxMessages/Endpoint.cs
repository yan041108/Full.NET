using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.SendHostInboxMessages;

internal static class Endpoint
{
    /// <summary>
    /// 注册 Host 站内信发送路由，允许管理员向指定用户投递站内信。
    /// </summary>
    /// <remarks>
    /// 绑定 <c>inbox.send</c> 权限；收件人必须为存在的 Host 用户，
    /// 站内信与实时修复事件同事务写入 Outbox，实时推送仅作低延迟广播。
    /// </remarks>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/host-inbox-messages")
            .WithTags("NotificationsHostInboxMessages");

        group.MapPost("/", async (
            SendHostInboxMessageRequest request,
            HostInboxMessageService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.SendAsync(userId, request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/notifications/my-inbox-messages/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("notificationsSendHostInboxMessage")
        .Produces<InboxMessageResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(InboxPermissions.Send));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
