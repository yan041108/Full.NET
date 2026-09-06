using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.ManageDingTalkApprovalSync;

internal static class Endpoint
{
    private const int MaxCallbackBodyBytes = 16 * 1024;

    /// <summary>注册钉钉审批镜像同步查询、登记、补偿与匿名回调入口。</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/dingtalk/approval-sync")
            .WithTags("NotificationsDingTalkApprovalSync");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            DingTalkApprovalSyncService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(page ?? 1, pageSize ?? 20, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsListDingTalkApprovalSync")
        .Produces<PagedResult<DingTalkApprovalSyncResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.DingTalkApprovalSyncRead));

        group.MapGet("/{syncId:guid}", async (
            Guid syncId,
            DingTalkApprovalSyncService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetByIdAsync(syncId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsGetDingTalkApprovalSync")
        .Produces<DingTalkApprovalSyncResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.DingTalkApprovalSyncRead));

        group.MapPost("/", async (
            CreateDingTalkApprovalSyncRequest request,
            DingTalkApprovalSyncService service,
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
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsCreateDingTalkApprovalSync")
        .Produces<DingTalkApprovalSyncResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.DingTalkApprovalSyncCreate));

        group.MapPost("/{syncId:guid}/retry", async (
            Guid syncId,
            DingTalkApprovalSyncService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RetryAsync(syncId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsRetryDingTalkApprovalSync")
        .Produces<DingTalkApprovalSyncResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(NotificationPlatformPermissions.DingTalkApprovalSyncRetry));

        endpoints.MapPost("/api/v1/notifications/dingtalk/approval-sync/callback", async (
            DingTalkApprovalSyncService service,
            DingTalkApprovalSyncCallbackVerifier verifier,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (httpContext.Request.ContentLength is { } contentLength
                && contentLength > MaxCallbackBodyBytes)
            {
                return mapper.Map(
                    Result<DingTalkApprovalSyncCallbackAcceptedResponse>.Failure(TooLarge()),
                    httpContext);
            }

            using var buffer = new MemoryStream();
            var block = new byte[8192];
            while (true)
            {
                var read = await httpContext.Request.Body.ReadAsync(block, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > MaxCallbackBodyBytes)
                {
                    return mapper.Map(
                        Result<DingTalkApprovalSyncCallbackAcceptedResponse>.Failure(TooLarge()),
                        httpContext);
                }

                buffer.Write(block, 0, read);
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in httpContext.Request.Headers)
            {
                headers[header.Key] = header.Value.ToString();
            }

            var verified = verifier.Verify(buffer.ToArray(), headers);
            if (!verified.IsSuccess)
            {
                return mapper.Map(
                    Result<DingTalkApprovalSyncCallbackAcceptedResponse>.Failure(verified.Error!),
                    httpContext);
            }

            var result = await service.ApplyCallbackAsync(verified.Value!, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsReceiveDingTalkApprovalSyncCallback")
        .WithTags("NotificationsDingTalkApprovalSync")
        .Produces<DingTalkApprovalSyncCallbackAcceptedResponse>(StatusCodes.Status200OK)
        .AllowAnonymous();
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }

    private static Error TooLarge() =>
        new(
            NotificationsErrorCodes.ReceiptTooLarge,
            "The approval sync callback payload exceeds the allowed size.",
            ErrorType.Validation);
}
