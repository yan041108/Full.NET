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

namespace Full.NET.Modules.EnterpriseRequest.Features.ApprovalProgress;

/// <summary>进度读取复用单据 Read 权限与组织数据范围，不扩大 Workflow 操作权限。</summary>
internal static class ApprovalProgressEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/enterprise_request/enterprise-requests/{id:guid}/approval-progress",
            async (Guid id, ClaimsPrincipal principal, EnterpriseRequestApprovalProgressService service,
                IApiResultMapper mapper, HttpContext context, CancellationToken cancellationToken) =>
            {
                if (!Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var actor) || actor == Guid.Empty)
                    return Results.Unauthorized();
                var isSuperAdministrator = bool.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.SuperAdministrator), out var super) && super;
                return mapper.Map(await service.GetAsync(id, actor, isSuperAdministrator, cancellationToken).ConfigureAwait(false), context);
            })
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Read))
            .WithTags("EnterpriseRequestEnterpriseRequests")
            .WithName("enterpriseRequestGetApprovalProgress")
            .Produces<EnterpriseRequestApprovalProgressResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}

/// <summary>审批进度 HTTP 线协议的静态 JSON 闭包。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(EnterpriseRequestApprovalProgressResponse))]
[JsonSerializable(typeof(EnterpriseRequestApprovalDeliveryState))]
internal partial class EnterpriseRequestApprovalProgressJsonContext : JsonSerializerContext;
