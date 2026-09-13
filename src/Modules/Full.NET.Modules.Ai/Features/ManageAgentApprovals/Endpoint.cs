using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Ai.Features.ManageAgentApprovals;

/// <summary>Agent 写工具审批创建、查询与决定端点。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/agent/approvals")
            .WithTags("AiAgentApprovals");

        group.MapPost("/", async (
            CreateAiAgentApprovalRequest request,
            AiAgentApprovalService service,
            ICurrentTenant tenant,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!AgentRunHttpBinding.TryCreate(httpContext, tenant.Id, out var binding))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(request, binding, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiCreateAgentApproval")
        .Produces<CreateAiAgentApprovalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentApprovalPermissions.Request));

        group.MapGet("/{approvalId:guid}", async (
            Guid approvalId,
            AiAgentApprovalService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.GetOwnedAsync(approvalId, userId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiGetAgentApproval")
        .Produces<AiAgentApprovalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentApprovalPermissions.Read));

        group.MapPost("/{approvalId:guid}/decide", async (
            Guid approvalId,
            DecideAiAgentApprovalRequest request,
            AiAgentApprovalService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.DecideAsync(approvalId, userId, request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiDecideAgentApproval")
        .Produces<AiAgentApprovalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentApprovalPermissions.Read));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
