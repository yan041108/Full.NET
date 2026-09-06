using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.QueryHostModuleSelection;

/// <summary>
/// 映射 Host 模块启用配置只读分析与校验端点。
/// </summary>
internal static class Endpoint
{
    /// <summary>
    /// 注册模块启用分析与校验路由。
    /// </summary>
    /// <param name="endpoints">宿主端点构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/modules/selection")
            .WithTags("IdentityHostModules");

        group.MapGet("/runtime", async (
            HostModuleSelectionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetRuntimeAsync(cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetModuleSelectionRuntime")
        .Produces<ModuleSelectionAnalysisResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(ModuleCatalogPermissions.Read));

        group.MapPost("/validate", async (
            ModuleSelectionValidateRequest request,
            HostModuleSelectionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ValidateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityValidateModuleSelection")
        .Produces<ModuleSelectionAnalysisResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(ModuleCatalogPermissions.Read));
    }
}
