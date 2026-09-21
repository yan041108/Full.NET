using System.Security.Claims;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AcceptTenantInvitation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageMyTenantInvitations;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/me/tenant-invitations",
                async (
                    HttpContext httpContext,
                    MyTenantInvitationQueryService queries,
                    IApiResultMapper mapper,
                    CancellationToken cancellationToken) =>
                {
                    if (!TryReadUserId(httpContext, out var userId))
                    {
                        return Results.Unauthorized();
                    }

                    var result = await queries.ListPendingAsync(userId, cancellationToken)
                        .ConfigureAwait(false);
                    return mapper.Map(result, httpContext);
                })
            .WithTags("IdentityMe")
            .WithName("identityListMyTenantInvitations")
            .Produces<IReadOnlyList<MyTenantInvitationResponse>>(StatusCodes.Status200OK)
            .RequireAuthorization();

        endpoints.MapPost(
                "/api/v1/me/tenant-invitations/{invitationId:guid}/accept",
                async (
                    Guid invitationId,
                    HttpContext httpContext,
                    AcceptTenantInvitationService service,
                    IApiResultMapper mapper,
                    CancellationToken cancellationToken) =>
                {
                    if (!TryReadUserId(httpContext, out var userId))
                    {
                        return Results.Unauthorized();
                    }

                    var result = await service.AcceptByIdAsync(userId, invitationId, cancellationToken)
                        .ConfigureAwait(false);
                    return mapper.Map(result, httpContext);
                })
            .WithTags("IdentityMe")
            .WithName("identityAcceptMyTenantInvitation")
            .Produces<AcceptTenantInvitationResponse>(StatusCodes.Status200OK)
            .RequireAuthorization();
    }

    private static bool TryReadUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(
            httpContext.User.FindFirstValue(FullNetIdentityClaimTypes.Subject),
            out userId);
}
