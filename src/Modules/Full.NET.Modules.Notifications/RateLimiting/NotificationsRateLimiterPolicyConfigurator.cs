using Full.NET.Abstractions.Results;
using Full.NET.Hosting.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace Full.NET.Modules.Notifications.RateLimiting;

/// <summary>匿名回执入口按 IP 分区限流，避免未验签前被无界打满。</summary>
internal sealed class NotificationsRateLimiterPolicyConfigurator :
    IConfigureOptions<RateLimiterOptions>,
    IConfigureOptions<RateLimitPolicyErrorCodes>
{
    public const int ReceiptPermitLimitPerMinute = 120;

    public void Configure(RateLimiterOptions rateLimiter) =>
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

    public void Configure(RateLimitPolicyErrorCodes registry) =>
        registry.MapPolicy(
            NotificationsModule.ProviderReceiptRateLimitPolicy,
            CommonErrorCodes.RateLimited);
}
