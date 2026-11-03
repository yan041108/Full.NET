using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ExecuteDefinitions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Reporting.Features.ManageDefinitionGrants;

/// <summary>管理 Host 发布授权与读取租户最小发布目录；两条路径使用独立权限和 SQL 边界。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reporting").WithTags("ReportingPublications");
        group.MapGet("/definitions/{definitionId:guid}/tenant-grants", async (Guid definitionId, int? page, int? pageSize,
            ReportingDefinitionGrantService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.ListAsync(definitionId, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context))
            .WithName("reportingListDefinitionTenantGrants")
            .Produces<PagedResult<ReportingDefinitionTenantGrantResponse>>()
            .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionGrantPermissions.Read));
        group.MapPut("/definitions/{definitionId:guid}/tenant-grants/{tenantId:guid}", async (Guid definitionId, Guid tenantId,
            SetReportingDefinitionTenantGrantRequest request, ReportingDefinitionGrantService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var actor))
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
            return mapper.Map(await service.SetAsync(definitionId, tenantId, request, actor, token).ConfigureAwait(false), context);
        }).WithName("reportingSetDefinitionTenantGrant")
            .Produces<ReportingDefinitionTenantGrantResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionGrantPermissions.Manage));
        group.MapGet("/published-definitions", async (string? nameContains, int? page, int? pageSize,
            ReportingExecutionCatalog catalog, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await catalog.ListAsync(nameContains, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context))
            .WithName("reportingListPublishedDefinitions")
            .Produces<PagedResult<ReportingPublishedDefinitionResponse>>()
            .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionGrantPermissions.PublishedRead));
    }
}
