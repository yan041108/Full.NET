using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;

internal static class Endpoint
{
    /// <summary>
    /// 注册 Host 公告管理路由组，包含列表、详情、创建、更新与发布操作。
    /// </summary>
    /// <remarks>
    /// 每个操作绑定独立稳定权限码并经 <c>RequireAuthorization</c> 强制校验；
    /// 创建、更新、发布使用乐观版本号做 CAS 并发控制，发布后由 Outbox 修复实时广播。
    /// </remarks>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/host-announcements")
            .WithTags("NotificationsHostAnnouncements");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            HostAnnouncementQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsListHostAnnouncements")
        .Produces<PagedResult<HostAnnouncementResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostAnnouncementPermissions.Read));

        group.MapGet("/{announcementId:guid}", async (
            Guid announcementId,
            HostAnnouncementQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(announcementId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostAnnouncementResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostAnnouncementPermissions.Read));

        group.MapPost("/", async (
            CreateHostAnnouncementRequest request,
            HostAnnouncementManagementService service,
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

            return Results.Created(
                $"/api/v1/notifications/host-announcements/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("notificationsCreateHostAnnouncement")
        .Produces<HostAnnouncementResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostAnnouncementPermissions.Create));

        group.MapPut("/{announcementId:guid}", async (
            Guid announcementId,
            UpdateHostAnnouncementRequest request,
            HostAnnouncementManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.UpdateAsync(
                    userId,
                    announcementId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsUpdateHostAnnouncement")
        .Produces<HostAnnouncementResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostAnnouncementPermissions.Update));

        group.MapPost("/{announcementId:guid}/publish", async (
            Guid announcementId,
            PublishHostAnnouncementRequest request,
            HostAnnouncementManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.PublishAsync(
                    userId,
                    announcementId,
                    request.Version,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsPublishHostAnnouncement")
        .Produces<HostAnnouncementResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostAnnouncementPermissions.Publish));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
