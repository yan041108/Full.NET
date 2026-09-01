using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.GetAvailableTenants;

/// <summary>
/// 暴露当前主体可切换租户列表端点。
/// </summary>
internal static class Endpoint
{
    /// <summary>
    /// 将可切换租户列表查询映射到 Tenancy 路由组。
    /// </summary>
    /// <param name="group">Tenancy 模块路由组。</param>
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/available", async (
                ClaimsPrincipal principal,
                IQueryDispatcher dispatcher,
                IApiResultMapper mapper,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                // 权限支持宿主演员进入租户后的有效作用域，但租户原生账号不能枚举平台租户。
                if (!string.Equals(
                        principal.FindFirstValue(FullNetIdentityClaimTypes.ActorScope),
                        "host",
                        StringComparison.Ordinal))
                {
                    return mapper.Map(
                        Result<TenantContextSummary[]>.Failure(new Error(
                            Code: IdentityErrorCodes.InvalidActorScope,
                            Message: "Only a Host actor can list available tenants.",
                            Type: ErrorType.Forbidden)),
                        httpContext);
                }

                var result = await dispatcher.SendAsync<Query, TenantContextSummary[]>(
                        new Query(),
                        cancellationToken)
                    .ConfigureAwait(false);
                return mapper.Map(result, httpContext);
            })
            .RequireAuthorization(FullNetPermissionPolicies.For(
                TenancyAuthorizationContributor.TenantsRead));
    }
}
