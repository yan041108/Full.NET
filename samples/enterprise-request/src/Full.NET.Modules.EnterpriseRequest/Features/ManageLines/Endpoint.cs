using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Hosting.Api;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.EnterpriseRequest.Features.ManageLines;

/// <summary>明细沿用聚合的精确 Read/Update 权限与组织边界。</summary>
internal static class EnterpriseRequestLinesEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        const string path = "/api/v1/enterprise_request/enterprise-requests/{id:guid}/lines";
        endpoints.MapGet(path, async (Guid id, ClaimsPrincipal principal, EnterpriseRequestLineService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken cancellationToken) => {
                if (!TryActor(principal, out var actor)) return Results.Unauthorized();
                var super = bool.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.SuperAdministrator), out var value) && value;
                return mapper.Map(await service.GetAsync(id, actor, super, cancellationToken).ConfigureAwait(false), context);
            })
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Read))
            .WithTags("EnterpriseRequestEnterpriseRequests").WithName("enterpriseRequestGetLines")
            .Produces<EnterpriseRequestLinesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        endpoints.MapPut(path, async (Guid id, ReplaceEnterpriseRequestLinesRequest body, ClaimsPrincipal principal,
            EnterpriseRequestLineService service, IApiResultMapper mapper, HttpContext context, CancellationToken cancellationToken) => {
                if (!TryActor(principal, out var actor)) return Results.Unauthorized();
                return mapper.Map(await service.ReplaceAsync(id, body, actor, cancellationToken).ConfigureAwait(false), context);
            })
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Update))
            .WithTags("EnterpriseRequestEnterpriseRequests").WithName("enterpriseRequestReplaceLines")
            .Produces<EnterpriseRequestLinesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
    private static bool TryActor(ClaimsPrincipal principal, out Guid actor) =>
        Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject), out actor) && actor != Guid.Empty;
}

/// <summary>明细输入、输出及集合的 Native AOT JSON 闭包。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ReplaceEnterpriseRequestLinesRequest))]
[JsonSerializable(typeof(EnterpriseRequestLinesResponse))]
internal partial class EnterpriseRequestLinesJsonContext : JsonSerializerContext;
