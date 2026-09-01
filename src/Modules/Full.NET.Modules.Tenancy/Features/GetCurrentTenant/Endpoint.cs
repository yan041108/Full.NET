using Full.NET.Abstractions.Messaging;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.GetCurrentTenant;

/// <summary>
/// 暴露当前请求租户上下文读取端点。
/// </summary>
internal static class Endpoint
{
    /// <summary>
    /// 将当前租户上下文查询映射到路由组。
    /// </summary>
    /// <param name="group">Tenancy 模块的路由组。</param>
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/current", async (
            IQueryDispatcher dispatcher,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatcher
                .SendAsync<GetCurrentTenantQuery, TenantSummary>(
                    new GetCurrentTenantQuery(),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .AllowAnonymous();
    }
}
