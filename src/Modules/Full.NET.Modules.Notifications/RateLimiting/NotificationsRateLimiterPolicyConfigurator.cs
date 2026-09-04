using Full.NET.Abstractions.Results;
using Full.NET.Hosting.RateLimiting;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace Full.NET.Modules.Notifications.RateLimiting;

/// <summary>Notifications 模块专用限流策略。</summary>
internal sealed class NotificationsRateLimiterPolicyConfigurator :
    IConfigureOptions<RateLimiterOptions>,
    IConfigureOptions<RateLimitPolicyErrorCodes>
{
    public const int ReceiptPermitLimitPerMinute = 120;
    public const int VerificationSendPermitLimitPerFifteenMinutes = 3;
    public const int VerificationVerifyPermitLimitPerFifteenMinutes = 10;

    public void Configure(RateLimiterOptions rateLimiter)
    {
        rateLimiter.AddPolicy(
            NotificationsModule.ProviderReceiptRateLimitPolicy,
            httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = ReceiptPermitLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        rateLimiter.AddPolicy(
            NotificationsModule.RecipientEndpointVerificationSendRateLimitPolicy,
            httpContext => RateLimitPartition.GetFixedWindowLimiter(
                BuildVerificationPartitionKey(httpContext),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = VerificationSendPermitLimitPerFifteenMinutes,
                    Window = TimeSpan.FromMinutes(15),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        rateLimiter.AddPolicy(
            NotificationsModule.RecipientEndpointVerificationVerifyRateLimitPolicy,
            httpContext => RateLimitPartition.GetFixedWindowLimiter(
                BuildVerificationPartitionKey(httpContext),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = VerificationVerifyPermitLimitPerFifteenMinutes,
                    Window = TimeSpan.FromMinutes(15),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    }

    public void Configure(RateLimitPolicyErrorCodes registry)
    {
        registry.MapPolicy(
            NotificationsModule.ProviderReceiptRateLimitPolicy,
            CommonErrorCodes.RateLimited);
        registry.MapPolicy(
            NotificationsModule.RecipientEndpointVerificationSendRateLimitPolicy,
            NotificationsErrorCodes.RecipientEndpointVerificationSendCooldown);
        registry.MapPolicy(
            NotificationsModule.RecipientEndpointVerificationVerifyRateLimitPolicy,
            CommonErrorCodes.RateLimited);
    }

    private static string BuildVerificationPartitionKey(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "anonymous";
        var endpointId = httpContext.Request.RouteValues.TryGetValue("endpointId", out var value)
            ? value?.ToString() ?? "unknown"
            : "unknown";
        return $"{userId}:{endpointId}";
    }
}
