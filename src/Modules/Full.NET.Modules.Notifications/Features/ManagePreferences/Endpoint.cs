using System.Security.Claims;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.ManagePreferences;

/// <summary>当前用户通知发送偏好 API；收件端点仍由独立端点管理 API 提供。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/my-preferences")
            .WithTags("NotificationsPreferences");

        // 仅公开合法渠道键，不让个人偏好页依赖渠道配置控制面的读取权限。
        group.MapGet("/channels", (Providers.INotificationProviderTypeCatalog catalog) =>
            Results.Ok<IReadOnlyList<string>>(
                new[] { Domain.NotificationTemplateCompiler.InboxChannelKey }
                    .Concat(catalog.All.SelectMany(item => item.SupportedChannelKeys))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray()))
            .WithName("notificationsListMyPreferenceChannels")
            .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(
                NotificationPlatformPermissions.PreferencesRead));

        group.MapGet("/", async (
            NotificationPreferenceService service,
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
        .WithName("notificationsListMyPreferences")
        .Produces<IReadOnlyList<NotificationPreferenceResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            NotificationPlatformPermissions.PreferencesRead));

        group.MapPut("/{channelKey}", async (
            string channelKey,
            UpdateMyNotificationPreferenceRequest request,
            NotificationPreferenceService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var normalized = request with { ChannelKey = channelKey };
            var result = await service.UpdateMineAsync(userId, normalized, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsUpdateMyPreference")
        .Produces<NotificationPreferenceResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            NotificationPlatformPermissions.PreferencesUpdate));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        var value = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId) && userId != Guid.Empty;
    }
}
