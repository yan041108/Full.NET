using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Middleware;

/// <summary>
/// OIDC 协议端点在认证中间件之前就需要 Host 租户上下文；Scoped 依赖必须注入 InvokeAsync 而非构造函数。
/// </summary>
internal sealed class IdentityOidcHostContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IOptions<IdentityOidcOptions> oidcOptions,
        ICurrentTenantContextWriter tenantContextWriter)
    {
        if (oidcOptions.Value.Enable && IdentityOidcProtocolPaths.IsProtocolPath(context.Request.Path))
        {
            tenantContextWriter.SetHost();
        }

        await next(context).ConfigureAwait(false);
    }
}