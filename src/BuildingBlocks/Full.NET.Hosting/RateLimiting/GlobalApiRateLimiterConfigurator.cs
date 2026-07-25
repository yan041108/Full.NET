using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.RateLimiting;

internal sealed class GlobalApiRateLimiterConfigurator(
    IOptions<RateLimitingOptions> options) : IConfigureOptions<RateLimiterOptions>
{
    public void Configure(RateLimiterOptions rateLimiter)
    {
        var settings = options.Value;
        if (!settings.EnableGlobalApiLimit || settings.GlobalApiPermitLimitPerMinute <= 0)
        {
            return;
        }

        rateLimiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            ResolvePartition);
    }

    private RateLimitPartition<string> ResolvePartition(HttpContext httpContext)
    {
        var settings = options.Value;
        var partitionKey = httpContext.User?.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.User.FindFirstValue("sub")
            : httpContext.Connection.RemoteIpAddress?.ToString();
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.GlobalApiPermitLimitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }
}
