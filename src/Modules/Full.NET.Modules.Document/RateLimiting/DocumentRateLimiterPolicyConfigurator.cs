using Full.NET.Abstractions.Results;
using Full.NET.Hosting.RateLimiting;
using Full.NET.Modules.Document.Configuration;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace Full.NET.Modules.Document.RateLimiting;

/// <summary>
/// 注册 Document 公开端点的专用限流策略，防止匿名分享口令暴力尝试。
/// </summary>
internal sealed class DocumentRateLimiterPolicyConfigurator(
    IConfiguration configuration) :
    IConfigureOptions<RateLimiterOptions>,
    IConfigureOptions<RateLimitPolicyErrorCodes>
{
    public void Configure(RateLimiterOptions rateLimiter)
    {
        var settings = configuration
            .GetSection(DocumentOptions.SectionName)
            .Get<DocumentOptions>() ?? new DocumentOptions();

        rateLimiter.AddPolicy(
            DocumentModule.AnonymousShareAccessRateLimitPolicy,
            httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.AnonymousShareAccessRateLimitPermitLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    }

    public void Configure(RateLimitPolicyErrorCodes registry)
    {
        registry.MapPolicy(
            DocumentModule.AnonymousShareAccessRateLimitPolicy,
            CommonErrorCodes.RateLimited);
    }
}
