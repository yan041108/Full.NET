using Full.NET.Abstractions.Observability;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Hosting.Observability;

/// <summary>供外层请求日志读取认证管道冻结的关联值；没有认证信息时只补充请求元数据。</summary>
public static class HttpLogCorrelation
{
    /// <summary>读取当前或已结束的关联作用域，结果不持有请求对象。</summary>
    /// <param name="context">宿主当前请求。</param>
    /// <returns>已取得的可信关联字段；缺失身份留空。</returns>
    public static LogCorrelationSnapshot Capture(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = context.Items.TryGetValue("FullNet.LogCorrelation", out var value)
            && value is LogCorrelationSnapshot captured ? captured : LogCorrelationContext.Capture();
        var fingerprint = HttpOperationLogSanitizer.FingerprintClientIp(context.Connection.RemoteIpAddress?.ToString());
        return snapshot with
        {
            RequestId = HttpOperationLogSanitizer.Truncate(context.TraceIdentifier, 128),
            ClientIpFingerprint = string.IsNullOrEmpty(fingerprint) ? null : fingerprint,
        };
    }
}
