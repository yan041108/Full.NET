using Full.NET.Hosting.RateLimiting;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace Full.NET.Modules.Identity.RateLimiting;

/// <summary>
/// 注册 Identity 认证与会话相关端点的专用限流策略。
/// </summary>
internal sealed class IdentityRateLimiterPolicyConfigurator(
    IConfiguration configuration) :
    IConfigureOptions<RateLimiterOptions>,
    IConfigureOptions<RateLimitPolicyErrorCodes>
{
    public void Configure(RateLimiterOptions rateLimiter)
    {
        var settings = configuration
            .GetSection(IdentityOptions.SectionName)
            .Get<IdentityOptions>() ?? new IdentityOptions();
        rateLimiter.AddPolicy("identity-login", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.LoginRateLimitPermitLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        rateLimiter.AddPolicy(IdentityModule.SessionMutationRateLimitPolicy, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.SessionMutationRateLimitPermitLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        rateLimiter.AddPolicy(
            "identity-super-administrator-write",
            httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        rateLimiter.AddPolicy(
            IdentityModule.SignatureAuthenticationRateLimitPolicy,
            httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Request.Headers[SignatureAuthenticationOptions.AccessKeyIdHeader]
                    .ToString()
                    is { Length: > 0 } accessKeyId
                    ? accessKeyId
                    : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    }

    public void Configure(RateLimitPolicyErrorCodes registry)
    {
        registry.MapPolicy("identity-login", IdentityErrorCodes.AuthenticationRateLimited);
        registry.MapPolicy(
            IdentityModule.SessionMutationRateLimitPolicy,
            IdentityErrorCodes.AuthenticationRateLimited);
        registry.MapPolicy(
            "identity-super-administrator-write",
            IdentityErrorCodes.AuthenticationRateLimited);
        registry.MapPolicy(
            IdentityModule.SignatureAuthenticationRateLimitPolicy,
            IdentityErrorCodes.AuthenticationRateLimited);
    }
}
