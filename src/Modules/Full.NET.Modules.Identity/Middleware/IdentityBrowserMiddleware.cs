using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Middleware;

/// <summary>BFF 凭据只在服务器内转为 Bearer，继续经过原有 JWT、权限、租户与权威会话校验。</summary>
internal sealed class IdentityBrowserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IOptions<IdentityBrowserOptions> options)
    {
        var path = context.Request.Path;
        var handle = context.Request.Cookies[IdentityBrowserSession.CookieName];
        if (!options.Value.Enable || string.IsNullOrEmpty(handle) || context.Request.Headers.ContainsKey("Authorization")
            || (!path.StartsWithSegments("/api") && !path.StartsWithSegments("/hubs"))
            || path.Equals("/api/v1/identity/browser/login") || path.Equals("/api/v1/identity/browser/callback"))
        { await next(context).ConfigureAwait(false); return; }

        var session = context.RequestServices.GetRequiredService<IdentityBrowserSession>();

        var unsafeMethod = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method);
        // Origin 先于票据读取及刷新；同站点兄弟来源也不能用 Cookie 驱动写操作或 WebSocket。
        if ((unsafeMethod || context.WebSockets.IsWebSocketRequest || context.Request.Headers.ContainsKey("Origin"))
            && !IsSameOrigin(context, options.Value.PublicOrigin))
        { await RejectAsync(context, 403, IdentityErrorCodes.BrowserOriginRejected).ConfigureAwait(false); return; }
        try
        {
            await session.LoadAsync(handle, context.RequestAborted, allowRefresh: !unsafeMethod).ConfigureAwait(false);
            if (unsafeMethod)
            {
                var actual = Encoding.UTF8.GetBytes(context.Request.Headers["X-CSRF-Token"].ToString());
                var expected = Encoding.UTF8.GetBytes(session.Ticket!.CsrfToken);
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                { await RejectAsync(context, 403, IdentityErrorCodes.BrowserCsrfRejected).ConfigureAwait(false); return; }
                await session.LoadAsync(handle, context.RequestAborted).ConfigureAwait(false);
            }
            if (HttpMethods.IsPut(context.Request.Method) && path.Equals("/api/v1/tenancy/context")
                && !await session.AcquireMutationAsync(context.RequestAborted).ConfigureAwait(false))
            { await RejectAsync(context, 409, IdentityErrorCodes.SessionContextConflict).ConfigureAwait(false); return; }
            // 禁止浏览器会话调用会返回原始 OAuth/legacy 凭据的入口。
            if (path.Equals("/api/v1/me/password") || path.StartsWithSegments("/api/v1/auth"))
            { await RejectAsync(context, 400, IdentityErrorCodes.BrowserEndpointRequired).ConfigureAwait(false); return; }
            context.Request.Headers.Authorization = "Bearer " + session.Ticket!.AccessToken;
            try { await next(context).ConfigureAwait(false); }
            finally { context.Request.Headers.Remove("Authorization"); }
        }
        catch (IdentityBrowserSessionUnavailableException error)
        { await RejectAsync(context, error.IsBusy ? 503 : 401, IdentityErrorCodes.BrowserSessionUnavailable).ConfigureAwait(false); }
        catch (OpenIddictExceptions.ProtocolException)
        { await RejectAsync(context, 401, IdentityErrorCodes.BrowserSessionUnavailable).ConfigureAwait(false); }
        catch (HttpRequestException)
        { await RejectAsync(context, 503, IdentityErrorCodes.BrowserSessionUnavailable).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        { await RejectAsync(context, 503, IdentityErrorCodes.BrowserSessionUnavailable).ConfigureAwait(false); }
        finally { await session.ReleaseAsync(CancellationToken.None).ConfigureAwait(false); }
    }

    internal static bool IsSameOrigin(HttpContext context, string configuredOrigin)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin)) return string.Equals(origin, configuredOrigin, StringComparison.Ordinal);
        return Uri.TryCreate(context.Request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
            && string.Equals(referer.GetLeftPart(UriPartial.Authority), configuredOrigin, StringComparison.Ordinal);
    }
    private static Task RejectAsync(HttpContext context, int status, string code) =>
        Results.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code })
            .ExecuteAsync(context);
}
