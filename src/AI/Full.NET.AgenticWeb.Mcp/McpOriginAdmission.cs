using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.AgenticWeb.Mcp;

/// <summary>在 MCP 端点执行前验证浏览器 Origin；非浏览器客户端可以不发送该头。</summary>
public static class McpOriginAdmission
{
    private static readonly PathString McpPath = new("/api/v1/ai/mcp");

    /// <summary>复用身份模块现有 CORS 来源策略，禁止未批准来源触达 MCP 协议处理器。</summary>
    public static IApplicationBuilder UseMcpOriginAdmission(this IApplicationBuilder app) =>
        app.Use(next => context => InvokeAsync(context, next));

    internal static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!(context.Request.Path == McpPath || context.Request.Path == McpPath.Add(new PathString("/")))
            || !context.Request.Headers.ContainsKey("Origin"))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var origins = context.Request.Headers.Origin;
        if (origins.Count != 1 || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin)
            || origin.Scheme is not ("http" or "https") || origin.UserInfo.Length != 0
            || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || !string.Equals(origins[0], origin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // 不能因 Origin 与请求 Host 相等而放行，否则可被 DNS 重绑定利用。
        var policies = context.RequestServices.GetRequiredService<ICorsPolicyProvider>();
        var policy = await policies.GetPolicyAsync(context, IdentityBrowserCorsPolicy.Name).ConfigureAwait(false);
        if (policy is null || !context.RequestServices.GetRequiredService<ICorsService>().EvaluatePolicy(context, policy).IsOriginAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await next(context).ConfigureAwait(false);
    }
}
