using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Organization.Features.ManageReportingLines;

/// <summary>全租户汇报关系采用独立精确权限，身份及 Tenant 由服务端解析。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        const string path = "/api/v1/organization/users/{employeeUserId:guid}/reporting-line";
        endpoints.MapGet(path, async (Guid employeeUserId, ClaimsPrincipal principal, OrganizationReportingLineService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) => {
            if (!OrganizationActorContext.TryResolve(principal, out var actor, out var super)) return Results.Unauthorized();
            return mapper.Map(await service.GetAsync(employeeUserId, actor, super, token).ConfigureAwait(false), context);
        }).WithTags("OrganizationReportingLines").WithName("organizationGetReportingLine").Produces<OrganizationReportingLineResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).RequireAuthorization(FullNetPermissionPolicies.For(OrganizationReportingLinePermissions.Read));
        endpoints.MapPut(path, async (Guid employeeUserId, SetOrganizationReportingLineRequest request, ClaimsPrincipal principal, OrganizationReportingLineService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) => {
            if (!OrganizationActorContext.TryResolve(principal, out var actor, out var super)) return Results.Unauthorized();
            return mapper.Map(await service.SetAsync(employeeUserId, actor, super, request, token).ConfigureAwait(false), context);
        }).WithTags("OrganizationReportingLines").WithName("organizationSetReportingLine").Produces<OrganizationReportingLineResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).RequireAuthorization(FullNetPermissionPolicies.For(OrganizationReportingLinePermissions.Update));
        endpoints.MapGet(path + "/candidates", async (Guid employeeUserId, int? page, int? pageSize, ClaimsPrincipal principal, OrganizationReportingLineService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) => {
            if (!OrganizationActorContext.TryResolve(principal, out var actor, out var super)) return Results.Unauthorized();
            return mapper.Map(await service.CandidatesAsync(employeeUserId, actor, super, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context);
        }).WithTags("OrganizationReportingLines").WithName("organizationListReportingCandidates").Produces<PagedResult<OrganizationReportingCandidate>>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).RequireAuthorization(FullNetPermissionPolicies.For(OrganizationReportingLinePermissions.Update));
    }
}
