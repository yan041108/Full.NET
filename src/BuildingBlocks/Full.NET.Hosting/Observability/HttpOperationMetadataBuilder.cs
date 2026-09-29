using System.Diagnostics;
using System.Globalization;
using Full.NET.Localization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 仅从受信连接、认证后的 Principal 与静态 Endpoint 元数据生成 B2 可用的请求摘要。
/// </summary>
internal static class HttpOperationMetadataBuilder
{
    public static HttpOperationContext Capture(
        HttpContext httpContext,
        bool captureThreadId = false,
        string? routeTemplate = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var endpoint = ResolveEndpoint(httpContext);
        routeTemplate ??= ResolveRouteTemplate(httpContext);
        var mvc = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        string? area = null;
        if (mvc is not null)
        {
            mvc.RouteValues.TryGetValue("area", out area);
        }
        var activity = Activity.Current;
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var sourceUrl = HttpOperationLogSanitizer.SanitizeSourceUrl(
            httpContext.Request.Headers.Origin.FirstOrDefault())
            ?? HttpOperationLogSanitizer.SanitizeSourceUrl(
                httpContext.Request.Headers.Referer.FirstOrDefault());
        var scheme = httpContext.Request.Scheme;
        if (scheme != Uri.UriSchemeHttp && scheme != Uri.UriSchemeHttps)
        {
            scheme = null;
        }

        return new HttpOperationContext(
            routeTemplate,
            Clean(endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName, 256),
            Clean(endpoint?.DisplayName, 256),
            Clean(mvc?.ControllerName, 128),
            Clean(mvc?.ActionName, 128),
            Clean(area, 128),
            SummarizeHttpMethod(httpContext.Request.Method),
            scheme,
            Clean(httpContext.Request.Protocol, 32),
            HttpOperationLogSanitizer.FingerprintUntrustedValue(sourceUrl),
            SummarizeUserAgent(httpContext.Request.Headers.UserAgent.FirstOrDefault()),
            SummarizeLocale(CultureInfo.CurrentUICulture.Name),
            SummarizeAcceptLanguage(httpContext.Request.Headers.AcceptLanguage.FirstOrDefault()),
            httpContext.User.Identity?.IsAuthenticated == true
                ? HttpOperationLogSanitizer.FingerprintUntrustedValue(
                    httpContext.User.FindFirst("client_id")?.Value)
                : null,
            string.IsNullOrWhiteSpace(remoteIp)
                ? null
                : HttpOperationLogSanitizer.FingerprintClientIp(remoteIp),
            activity?.TraceId.ToString(),
            activity?.SpanId.ToString(),
            Clean(httpContext.TraceIdentifier, 64) ?? string.Empty,
            captureThreadId ? Environment.CurrentManagedThreadId : null);
    }

    public static string ResolveRouteTemplate(HttpContext httpContext)
        => HttpOperationLogSanitizer.Truncate(ResolveFullRouteTemplate(httpContext), 256);

    public static string ResolveFullRouteTemplate(HttpContext httpContext)
    {
        var endpoint = ResolveEndpoint(httpContext);
        return endpoint is RouteEndpoint routeEndpoint
            && !string.IsNullOrWhiteSpace(routeEndpoint.RoutePattern.RawText)
                ? routeEndpoint.RoutePattern.RawText
                : "<unmatched>";
    }

    private static Endpoint? ResolveEndpoint(HttpContext httpContext) =>
        httpContext.GetEndpoint()
        ?? httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint;

    private static string SummarizeHttpMethod(string? method)
    {
        if (string.IsNullOrEmpty(method) || method.Length > 16)
        {
            return "OTHER";
        }

        // HTTP 方法来自请求行，只允许固定协议类别进入 B2。
        return method.ToUpperInvariant() switch
        {
            "GET" => "GET",
            "HEAD" => "HEAD",
            "POST" => "POST",
            "PUT" => "PUT",
            "PATCH" => "PATCH",
            "DELETE" => "DELETE",
            "OPTIONS" => "OPTIONS",
            "TRACE" => "TRACE",
            "CONNECT" => "CONNECT",
            _ => "OTHER",
        };
    }

    private static string? SummarizeUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Length > 4096)
        {
            return null;
        }

        // 只输出固定浏览器族；原始 UA 是请求方可填写的任意文本。
        var bounded = userAgent.AsSpan(0, Math.Min(userAgent.Length, 512));
        if (bounded.Contains("Edg/", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (bounded.Contains("Firefox/", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (bounded.Contains("Chrome/", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (bounded.Contains("Safari/", StringComparison.OrdinalIgnoreCase)) return "Safari";
        if (bounded.Contains("curl/", StringComparison.OrdinalIgnoreCase)) return "curl";
        return "Other";
    }

    private static string? SummarizeAcceptLanguage(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage) || acceptLanguage.Length > 1024)
        {
            return null;
        }

        var first = acceptLanguage.AsSpan();
        var comma = first.IndexOf(',');
        if (comma >= 0) first = first[..comma];
        var quality = first.IndexOf(';');
        if (quality >= 0) first = first[..quality];
        return first.Length > 64 ? null : SummarizeLocale(first.Trim().ToString());
    }

    private static string? SummarizeLocale(string? locale)
    {
        foreach (var supported in LocaleCatalog.SupportedLocales)
        {
            if (string.Equals(locale, supported, StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        return null;
    }

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var length = Math.Min(value.Length, maxLength);
        if (length < value.Length
            && length > 0
            && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        var bounded = length == value.Length ? value : value[..length];
        var cleaned = HttpOperationLogSanitizer.StripControlChars(bounded);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
