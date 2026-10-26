using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Organization.Features.ManageUnitLeadership;

/// <summary>领导权独立接口只接受认证人和当前可信租户；读配置、选候选和写配置分别声明准确权限。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        const string path = "/api/v1/organization/units/{unitId:guid}/leadership";
        endpoints.MapGet(path, async (Guid unitId, ClaimsPrincipal principal, OrganizationUnitLeadershipService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => {
            if (!OrganizationActorContext.TryResolve(principal, out var actor, out var super)) return Results.Unauthorized();
            return mapper.Map(await service.GetAsync(unitId, actor, super, token).ConfigureAwait(false), context);
        }).WithTags("OrganizationUnitLeadership").WithName("organizationGetTenantUnitLeadership").Produces<OrganizationUnitLeadershipResponse>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(OrganizationUnitLeadershipPermissions.Read));
        endpoints.MapGet(path + "/candidates", async (Guid unitId, int? page, int? pageSize, ClaimsPrincipal principal,
            OrganizationUnitLeadershipService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) => {
            if (!OrganizationActorContext.TryResolve(principal, out var actor, out var super)) return Results.Unauthorized();
            return mapper.Map(await service.CandidatesAsync(unitId, actor, super, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context);
        }).WithTags("OrganizationUnitLeadership").WithName("organizationListTenantUnitLeaderCandidates").Produces<PagedResult<OrganizationUnitLeaderCandidateResponse>>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(OrganizationUnitLeadershipPermissions.Update));
        endpoints.MapPut(path, async (Guid unitId, SetOrganizationUnitLeadershipRequest request, ClaimsPrincipal principal,
            OrganizationUnitLeadershipService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) => {
            if (!OrganizationActorContext.TryResolve(principal, out var actor, out var super)) return Results.Unauthorized();
            return mapper.Map(await service.SetAsync(unitId, actor, super, request, token).ConfigureAwait(false), context);
        }).WithTags("OrganizationUnitLeadership").WithName("organizationSetTenantUnitLeadership").Produces<OrganizationUnitLeadershipResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(OrganizationUnitLeadershipPermissions.Update));
    }
}
