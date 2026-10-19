using Full.NET.Abstractions.Observability;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Identity.Middleware;

/// <summary>包裹认证与业务请求，采集时读取认证后的主体和租户；匿名请求只记录请求与网络指纹。</summary>
internal sealed class LogCorrelationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using var scope = LogCorrelationContext.BeginScope(() => Capture(context));
        try { await next(context).ConfigureAwait(false); }
        finally
        {
            // 外层 HTTP 完成日志在作用域退出后产生，只能读取已冻结的值。
            context.Items["FullNet.LogCorrelation"] = LogCorrelationContext.Capture();
        }
    }

    internal static LogCorrelationSnapshot Capture(HttpContext context)
    {
        var authenticated = context.User.Identity?.IsAuthenticated == true;
        Guid? Claim(string name) => authenticated && Guid.TryParse(context.User.FindFirst(name)?.Value, out var id)
            && id != Guid.Empty ? id : null;
        var fingerprint = HttpOperationLogSanitizer.FingerprintClientIp(context.Connection.RemoteIpAddress?.ToString());
        return new LogCorrelationSnapshot
        {
            RequestId = HttpOperationLogSanitizer.Truncate(context.TraceIdentifier, 128),
            UserId = Claim(FullNetIdentityClaimTypes.Subject),
            SessionId = Claim(FullNetIdentityClaimTypes.SessionId),
            CenterSessionId = Claim(FullNetIdentityClaimTypes.CenterSessionId),
            ApplicationSessionId = Claim(FullNetIdentityClaimTypes.ApplicationSessionId),
            TenantId = context.Items.TryGetValue("FullNet.TenantId", out var tenant) && tenant is Guid id ? id : null,
            ClientIpFingerprint = string.IsNullOrEmpty(fingerprint) ? null : fingerprint,
        };
    }
}
