using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.ChangeTenantContext;

/// <summary>
/// 暴露租户上下文切换端点。
/// </summary>
internal static class Endpoint
{
    /// <summary>
    /// 将租户上下文切换入口映射到 Tenancy 路由组。
    /// </summary>
    /// <param name="group">Tenancy 模块路由组。</param>
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/context", async (
                ChangeTenantContextRequest request,
                ClaimsPrincipal principal,
                ICommandDispatcher dispatcher,
                IApiResultMapper mapper,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.SendAsync<
                        Command,
                        TenantContextTokenResponse>(
                        new Command(request.TenantId, principal),
                        cancellationToken)
                    .ConfigureAwait(false);
                return mapper.Map(result, httpContext);
            })
            .RequireAuthorization(FullNetPermissionPolicies.For(
                TenancyAuthorizationContributor.TenantsSwitch));
    }
}
