using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

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
        if (oidcOptions.Value.Enable && (IdentityOidcProtocolPaths.IsProtocolPath(context.Request.Path)
            || context.Request.Path.Equals("/api/v1/identity/browser/login")
            || context.Request.Path.Equals("/api/v1/identity/browser/callback")))
        {
            tenantContextWriter.SetHost();
        }

        var signing = context.Request.Path.Equals("/connect/authorize") || context.Request.Path.Equals("/connect/token")
            || context.Request.Path.Equals("/api/v1/tenancy/context");
        var firstTrust = oidcOptions.Value.Enable && !context.Request.Path.StartsWithSegments("/health")
            && !context.RequestServices.GetRequiredService<IdentityOidcSigningKeyRing>().GovernanceLoaded;
        if (oidcOptions.Value.Enable && (signing || firstTrust))
        {
            // 首次建立信任前加载退役状态；之后公开 JWKS 可继续提供已知材料，不因数据库故障清空。
            var wasHost = tenantContextWriter.IsHost;
            var previous = tenantContextWriter.Id is Guid id ? new TenantContext(id, tenantContextWriter.Identifier!, tenantContextWriter.Name!) : null;
            tenantContextWriter.SetHost();
            try
            {
                await context.RequestServices.GetRequiredService<IdentityOidcSigningPolicy>()
                    .SynchronizeAsync(context.RequestAborted).ConfigureAwait(false);
            }
            finally
            {
                if (!signing)
                {
                    if (previous is not null) tenantContextWriter.SetTenant(previous);
                    else if (wasHost) tenantContextWriter.SetHost(); else tenantContextWriter.Clear();
                }
            }
        }

        await next(context).ConfigureAwait(false);
    }
}
