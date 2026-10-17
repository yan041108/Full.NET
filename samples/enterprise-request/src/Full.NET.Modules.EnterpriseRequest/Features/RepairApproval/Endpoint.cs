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

namespace Full.NET.Modules.EnterpriseRequest.Features.RepairApproval;

/// <summary>恢复单独授权并保留业务读取范围；租户、操作者和目标状态不能由请求提供。</summary>
internal static class EnterpriseRequestApprovalRepairEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v1/enterprise_request/enterprise-requests/{id:guid}/repair-approval",
        async (Guid id, RepairEnterpriseRequestApprovalRequest request, ClaimsPrincipal principal,
            EnterpriseRequestApprovalRepairService service, IApiResultMapper mapper, HttpContext context, CancellationToken ct) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var actor) || actor == Guid.Empty)
                return Results.Unauthorized();
            var super = bool.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.SuperAdministrator), out var isSuper) && isSuper;
            var result = await service.RepairAsync(id, request, actor, super, ct).ConfigureAwait(false);
            return result.IsSuccess ? Results.NoContent() : mapper.Map(result, context);
        })
        .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Read),
            FullNetPermissionPolicies.For(EnterpriseRequestWorkflowPermissions.RepairApproval))
        .WithTags("EnterpriseRequestEnterpriseRequests").WithName("enterpriseRequestRepairApproval")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
}

/// <summary>恢复命令保持静态 JSON 元数据，与申请进度响应分别注册。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RepairEnterpriseRequestApprovalRequest))]
internal partial class EnterpriseRequestApprovalRepairJsonContext : JsonSerializerContext;
