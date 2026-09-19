using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.RegistrationInvitations;

internal static class Endpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/invitations/verify", async (
            VerifyRegistrationInvitationRequest request,
            RegistrationInvitationService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.VerifyAsync(
                    request.InvitationId,
                    request.InvitationToken,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityVerifyRegistrationInvitation")
        .Produces<VerifyRegistrationInvitationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .AllowAnonymous()
        .RequireRateLimiting(IdentityModule.SessionMutationRateLimitPolicy);
    }
}
