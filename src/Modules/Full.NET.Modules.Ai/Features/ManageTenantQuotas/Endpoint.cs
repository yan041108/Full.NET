using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageTenantQuotas;

/// <summary>AI 租户配额管理 HTTP 端点。</summary>
internal static class Endpoint
{
    /// <summary>注册租户配额管理路由。</summary>
    /// <param name="endpoints">路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/tenant-quotas")
            .WithTags("AiTenantQuotas");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? tenantId,
            AiTenantQuotaQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    tenantId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiListTenantQuotas")
        .Produces<PagedResult<AiTenantQuotaListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiTenantQuotaPermissions.Read));

        group.MapGet("/{tenantId:guid}", async (
            Guid tenantId,
            AiTenantQuotaQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByTenantIdAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiGetTenantQuota")
        .Produces<AiTenantQuotaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiTenantQuotaPermissions.Read));

        group.MapPut("/{tenantId:guid}", async (
            Guid tenantId,
            UpdateAiTenantQuotaRequest request,
            AiTenantQuotaManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpsertAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiUpsertTenantQuota")
        .Produces<AiTenantQuotaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiTenantQuotaPermissions.Update));
    }
}
