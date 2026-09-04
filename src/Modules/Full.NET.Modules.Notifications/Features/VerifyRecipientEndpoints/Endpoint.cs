using System.Security.Claims;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.VerifyRecipientEndpoints;

/// <summary>注册收件端点邮件验证码发送与校验路由。</summary>
internal static class Endpoint
{
    /// <summary>映射当前用户收件端点验证 API。</summary>
    /// <param name="endpoints">应用程序路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/my-recipient-endpoints")
            .WithTags("NotificationsRecipientEndpoints");

        group.MapPost("/{endpointId:guid}/verification/send", async (
            Guid endpointId,
            RecipientEndpointVerificationService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.SendCodeAsync(userId, endpointId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsSendMyRecipientEndpointVerification")
        .Produces<SendRecipientEndpointVerificationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            NotificationPlatformPermissions.PreferencesUpdate))
        .RequireRateLimiting(NotificationsModule.RecipientEndpointVerificationSendRateLimitPolicy);

        group.MapPost("/{endpointId:guid}/verification/verify", async (
            Guid endpointId,
            VerifyRecipientEndpointCodeRequest request,
            RecipientEndpointVerificationService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.VerifyCodeAsync(userId, endpointId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsVerifyMyRecipientEndpoint")
        .Produces<RecipientEndpointResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            NotificationPlatformPermissions.PreferencesUpdate))
        .RequireRateLimiting(NotificationsModule.RecipientEndpointVerificationVerifyRateLimitPolicy);
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        var value = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId) && userId != Guid.Empty;
    }
}
