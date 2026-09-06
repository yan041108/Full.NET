using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.ManageMyHostAnnouncements;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/my-host-announcements")
            .WithTags("NotificationsMyHostAnnouncements");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? title,
            bool? isRead,
            MyHostAnnouncementQueryService queries,
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
                    new ReceivedHostAnnouncementListFilter(title, isRead),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsListMyHostAnnouncements")
        .Produces<PagedResult<ReceivedHostAnnouncementListItemResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostAnnouncementPermissions.ReceivedRead));

        group.MapGet("/unread-count", async (
            MyHostAnnouncementQueryService queries,
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
        .WithName("notificationsGetMyHostAnnouncementUnreadCount")
        .Produces<HostAnnouncementUnreadCountResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostAnnouncementPermissions.ReceivedRead));

        group.MapGet("/{announcementId:guid}", async (
            Guid announcementId,
            MyHostAnnouncementQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await queries.GetByIdAsync(userId, announcementId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsGetMyHostAnnouncement")
        .Produces<ReceivedHostAnnouncementDetailResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostAnnouncementPermissions.ReceivedRead));

        group.MapPost("/{announcementId:guid}/read", async (
            Guid announcementId,
            MyHostAnnouncementManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.MarkReadAsync(userId, announcementId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsMarkMyHostAnnouncementRead")
        .Produces<ReceivedHostAnnouncementDetailResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostAnnouncementPermissions.ReceivedMarkRead));

        group.MapPost("/read-all", async (
            MyHostAnnouncementManagementService service,
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
        .WithName("notificationsMarkAllMyHostAnnouncementsRead")
        .Produces<HostAnnouncementUnreadCountResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostAnnouncementPermissions.ReceivedMarkAllRead));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
