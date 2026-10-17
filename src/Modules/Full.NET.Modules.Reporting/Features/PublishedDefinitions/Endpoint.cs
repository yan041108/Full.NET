using Full.NET.Hosting.Api;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Reporting.Features.PublishedDefinitions;

/// <summary>租户发布目录与 Host 精确版本授权端点。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/reporting/published-definitions", async (
            ReportingPublishedDefinitionResolver resolver, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await resolver.ListAsync(token).ConfigureAwait(false), context))
            .WithTags("ReportingDefinitions").WithName("reportingListPublishedDefinitions")
            .Produces<IReadOnlyList<ReportingPublishedDefinitionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(ReportingExecutionPermissions.Run));

        var route = "/api/v1/reporting/definitions/{definitionId:guid}/versions/{versionNumber:int}/tenant-grants/{tenantId:guid}";
        endpoints.MapGet("/api/v1/reporting/definitions/{definitionId:guid}/versions/{versionNumber:int}/tenant-grants",
            async (Guid definitionId, int versionNumber, int? page, int? pageSize,
                ReportingTenantGrantManagementService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
                mapper.Map(await service.ListAsync(definitionId, versionNumber, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context))
            .WithTags("ReportingDefinitions").WithName("reportingListTenantVersionGrants")
            .Produces<PagedResult<Guid>>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionPermissions.GrantTenants));
        endpoints.MapPut(route, (Guid definitionId, int versionNumber, Guid tenantId,
            ReportingTenantGrantManagementService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            SetAsync(definitionId, versionNumber, tenantId, true, service, mapper, context, token))
            .WithTags("ReportingDefinitions").WithName("reportingGrantTenantVersion").Produces<bool>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionPermissions.GrantTenants));
        endpoints.MapDelete(route, (Guid definitionId, int versionNumber, Guid tenantId,
            ReportingTenantGrantManagementService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            SetAsync(definitionId, versionNumber, tenantId, false, service, mapper, context, token))
            .WithTags("ReportingDefinitions").WithName("reportingRevokeTenantVersion").Produces<bool>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionPermissions.GrantTenants));
    }

    private static async Task<IResult> SetAsync(Guid definitionId, int version, Guid tenantId, bool grant,
        ReportingTenantGrantManagementService service, IApiResultMapper mapper, HttpContext context, CancellationToken token)
    {
        if (!Guid.TryParse(context.User.FindFirst(FullNetIdentityClaimTypes.Subject)?.Value, out var actor))
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        return mapper.Map(await service.SetAsync(definitionId, version, tenantId, actor, grant, token).ConfigureAwait(false), context);
    }
}
