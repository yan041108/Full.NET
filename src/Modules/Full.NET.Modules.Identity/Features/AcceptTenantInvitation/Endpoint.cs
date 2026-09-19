using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

namespace Full.NET.Modules.Identity.Features.AcceptTenantInvitation;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/identity/tenant-invitations/accept",
                async (
                    AcceptTenantInvitationRequest request,
                    AcceptTenantInvitationService service,
                    IApiResultMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    if (!Guid.TryParse(
                            httpContext.User.FindFirstValue(FullNetIdentityClaimTypes.Subject),
                            out var userId))
                    {
                        return Results.Unauthorized();
                    }

                    var result = await service.AcceptAsync(userId, request, cancellationToken)
                        .ConfigureAwait(false);
                    return mapper.Map(result, httpContext);
                })
            .WithTags("IdentityTenantMembers")
            .WithName("identityAcceptTenantInvitation")
            .Produces<AcceptTenantInvitationResponse>(StatusCodes.Status200OK)
            .RequireAuthorization();
    }
}
