using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Ai.Features.ManageAgentDelegations;

/// <summary>持久委托创建、列出与撤销端点。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/agent/delegations")
            .WithTags("AiAgentDelegations");

        group.MapPost("/", async (
            CreateAiAgentDelegationRequest request,
            AiAgentDelegationService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(request, userId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiCreateAgentDelegation")
        .Produces<CreateAiAgentDelegationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentApprovalPermissions.Delegate));

        group.MapGet("/", async (
            AiAgentDelegationService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.ListOwnedAsync(userId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiListAgentDelegations")
        .Produces<IReadOnlyList<AiAgentDelegationResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentApprovalPermissions.Delegate));

        group.MapPost("/{delegationId:guid}/revoke", async (
            Guid delegationId,
            RevokeAiAgentDelegationRequest request,
            AiAgentDelegationService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.RevokeAsync(delegationId, userId, request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiRevokeAgentDelegation")
        .Produces<AiAgentDelegationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentApprovalPermissions.Delegate));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
