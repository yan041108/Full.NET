using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Auditing.Middleware;

/// <summary>
/// HTTP 操作日志中间件。记录已认证用户的写操作请求（POST/PUT/PATCH/DELETE，排除 /health /openapi /scalar 等非业务端点），
/// 汇总行包含 ActionKey、HTTP 方法、路径、状态码、耗时、用户/租户、TraceId、IP 指纹、权限码等，
/// 最终经 OperationLogWriter 捕获 → AuditWriteBuffer 缓冲 → AuditWriteCoordinatorMiddleware 按 B0/B1 分级写入：
/// B0（同事务域内关键变更）同步落库、B1（常规操作）走有界 Channel 异步批量落库；写入失败不影响业务响应。
/// </summary>
internal sealed class OperationLogMiddleware(RequestDelegate next)
{
    private const string TenantItemKey = "FullNet.TenantId";
    private const int MaxPathLength = 512;
    private const int MaxActionKeyLength = 256;
    private const int MaxPermissionLength = 128;

    private static readonly HashSet<string> MutationMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
    };

    public async Task InvokeAsync(HttpContext httpContext, OperationLogWriter writer)
    {
        if (!ShouldCapture(httpContext))
        {
            await next(httpContext).ConfigureAwait(false);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            var model = BuildModel(httpContext, stopwatch.Elapsed);
            writer.Capture(model);
        }
    }

    private static bool ShouldCapture(HttpContext httpContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (!MutationMethods.Contains(httpContext.Request.Method))
        {
            return false;
        }

        var path = httpContext.Request.Path;
        if (!path.StartsWithSegments("/api"))
        {
            return false;
        }

        if (path.StartsWithSegments("/health")
            || path.StartsWithSegments("/openapi")
            || path.StartsWithSegments("/scalar"))
        {
            return false;
        }

        return true;
    }

    private static OperationLogWriteModel BuildModel(HttpContext httpContext, TimeSpan elapsed)
    {
        Guid? userId = null;
        var subject = httpContext.User.FindFirst(FullNetIdentityClaimTypes.Subject)?.Value;
        if (Guid.TryParse(subject, out var parsedUserId))
        {
            userId = parsedUserId;
        }

        Guid? tenantId = null;
        if (httpContext.Items.TryGetValue(TenantItemKey, out var tenantValue)
            && tenantValue is Guid parsedTenantId)
        {
            tenantId = parsedTenantId;
        }

        var method = httpContext.Request.Method;
        if (method.Length > 16)
        {
            method = method[..16];
        }

        var path = httpContext.Request.Path.Value ?? "/";
        if (path.Length > MaxPathLength)
        {
            path = path[..MaxPathLength];
        }

        var actionKey = $"{method} {path}";
        if (actionKey.Length > MaxActionKeyLength)
        {
            actionKey = actionKey[..MaxActionKeyLength];
        }

        var permissionCode = httpContext.User
            .FindFirst(FullNetIdentityClaimTypes.Permission)?.Value;
        if (permissionCode is { Length: > MaxPermissionLength })
        {
            permissionCode = permissionCode[..MaxPermissionLength];
        }

        var traceId = Activity.Current?.TraceId.ToString()
            ?? httpContext.TraceIdentifier;
        if (traceId.Length > 64)
        {
            traceId = traceId[..64];
        }

        var durationMs = elapsed.TotalMilliseconds > int.MaxValue
            ? int.MaxValue
            : (int)Math.Round(elapsed.TotalMilliseconds);
        var statusCode = httpContext.Response.StatusCode;

        return new OperationLogWriteModel(
            actionKey,
            method,
            path,
            statusCode,
            durationMs,
            statusCode < StatusCodes.Status400BadRequest,
            userId,
            tenantId,
            string.IsNullOrWhiteSpace(traceId) ? null : traceId,
            FingerprintClientIp(httpContext),
            string.IsNullOrWhiteSpace(permissionCode) ? null : permissionCode);
    }

    private static string? FingerprintClientIp(HttpContext httpContext)
    {
        var address = httpContext.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(address)));
    }
}
