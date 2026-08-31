using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.ManageProviderProfiles;

internal static class Endpoint
{
    /// <summary>注册渠道配置路由；请求体不得覆盖 TenantId，也不得提交明文 Secret。</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/notifications/provider-types", async (
            NotificationProviderProfileService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListTypesAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsListProviderTypes")
        .WithTags("NotificationsProviderProfiles")
        .Produces<IReadOnlyList<NotificationProviderTypeDescriptor>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.ProviderProfilesRead));

        var group = endpoints.MapGroup("/api/v1/notifications/provider-profiles")
            .WithTags("NotificationsProviderProfiles");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            NotificationProviderProfileService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(page ?? 1, pageSize ?? 20, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsListProviderProfiles")
        .Produces<PagedResult<NotificationProviderProfileResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.ProviderProfilesRead));

        group.MapGet("/{profileId:guid}", async (
            Guid profileId,
            NotificationProviderProfileService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetByIdAsync(profileId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsGetProviderProfile")
        .Produces<NotificationProviderProfileResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.ProviderProfilesRead));

        group.MapPost("/", async (
            CreateNotificationProviderProfileRequest request,
            NotificationProviderProfileService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(userId, request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/notifications/provider-profiles/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("notificationsCreateProviderProfile")
        .Produces<NotificationProviderProfileResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.ProviderProfilesCreate));

        group.MapPut("/{profileId:guid}", async (
            Guid profileId,
            UpdateNotificationProviderProfileRequest request,
            NotificationProviderProfileService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.UpdateAsync(userId, profileId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsUpdateProviderProfile")
        .Produces<NotificationProviderProfileResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.ProviderProfilesUpdate));

        group.MapPost("/{profileId:guid}/publish", async (
            Guid profileId,
            PublishNotificationProviderProfileRequest request,
            NotificationProviderProfileService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.PublishAsync(userId, profileId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsPublishProviderProfile")
        .Produces<NotificationProviderProfileResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.ProviderProfilesPublish));

        group.MapPost("/{profileId:guid}/enable", async (
            Guid profileId,
            SetNotificationProviderProfileEnabledRequest request,
            NotificationProviderProfileService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.SetEnabledAsync(userId, profileId, true, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsEnableProviderProfile")
        .Produces<NotificationProviderProfileResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.ProviderProfilesEnable));

        group.MapPost("/{profileId:guid}/disable", async (
            Guid profileId,
            SetNotificationProviderProfileEnabledRequest request,
            NotificationProviderProfileService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.SetEnabledAsync(userId, profileId, false, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsDisableProviderProfile")
        .Produces<NotificationProviderProfileResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.ProviderProfilesDisable));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
