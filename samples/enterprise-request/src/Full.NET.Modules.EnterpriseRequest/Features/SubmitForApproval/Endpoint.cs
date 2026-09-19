using System.Security.Claims;
using Full.NET.Hosting.Api;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval;

internal static class SubmitForApprovalEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/enterprise_request/enterprise-requests/{id:guid}/submit-for-approval",
                async (
                    Guid id,
                    ClaimsPrincipal principal,
                    SubmitEnterpriseRequestForApprovalService service,
                    IApiResultMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    if (!TryResolveActor(principal, out var actorUserId))
                    {
                        return Results.Unauthorized();
                    }

                    var result = await service.SubmitAsync(id, actorUserId, cancellationToken)
                        .ConfigureAwait(false);
                    return mapper.Map(result, httpContext);
                })
            .RequireAuthorization()
            .WithTags("EnterpriseRequestEnterpriseRequests")
            .WithName("submitEnterpriseRequestForApproval");
    }

    private static bool TryResolveActor(ClaimsPrincipal principal, out Guid actorUserId)
    {
        actorUserId = default;
        return Guid.TryParse(
            principal.FindFirstValue(FullNetIdentityClaimTypes.Subject),
            out actorUserId);
    }
}