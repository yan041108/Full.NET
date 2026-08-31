using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Notifications.Features.ReceiveProviderReceipts;

internal static class Endpoint
{
    /// <summary>
    /// 注册 Provider 专用匿名回执入口；只读原始 Body 做验签，正文不进入模型绑定或日志。
    /// </summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/notifications/provider-receipts/{providerTypeKey}", async (
            string providerTypeKey,
            NotificationReceiptProcessor processor,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (httpContext.Request.ContentLength is { } contentLength
                && contentLength > NotificationReceiptProcessor.MaxBodyBytes)
            {
                return mapper.Map(
                    Result<NotificationReceiptAcceptedResponse>.Failure(TooLarge()),
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

                if (buffer.Length + read > NotificationReceiptProcessor.MaxBodyBytes)
                {
                    return mapper.Map(
                        Result<NotificationReceiptAcceptedResponse>.Failure(TooLarge()),
                        httpContext);
                }

                buffer.Write(block, 0, read);
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in httpContext.Request.Headers)
            {
                headers[header.Key] = header.Value.ToString();
            }

            var result = await processor.ProcessAsync(
                    providerTypeKey,
                    buffer.ToArray(),
                    headers,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("notificationsReceiveProviderReceipt")
        .WithTags("NotificationsProviderReceipts")
        .Produces<NotificationReceiptAcceptedResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(NotificationsModule.ProviderReceiptRateLimitPolicy)
        .AllowAnonymous();
    }

    private static Error TooLarge() =>
        new(
            NotificationsErrorCodes.ReceiptTooLarge,
            "The receipt payload exceeds the allowed size.",
            ErrorType.Validation);
}
