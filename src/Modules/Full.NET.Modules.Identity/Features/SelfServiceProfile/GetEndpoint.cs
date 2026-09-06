using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.SelfServiceProfile;

internal static class GetEndpoint
{
    /// <summary>映射当前用户自助档案读取端点。</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/me/profile", async (
            ClaimsPrincipal principal,
            SelfServiceProfileService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadIdentity(principal, out var userId, out var actorScope))
            {
                return mapper.Map(Unauthorized(), httpContext);
            }

            var result = await service.GetAsync(userId, actorScope, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetSelfServiceProfile")
        .WithTags("IdentityMe")
        .Produces<SelfServiceProfileResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization();
    }

    private static bool TryReadIdentity(
        ClaimsPrincipal principal,
        out Guid userId,
        out string actorScope)
    {
        actorScope = principal.FindFirstValue(IdentityClaimTypes.ActorScope) ?? string.Empty;
        return Guid.TryParse(
                   principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                   out userId)
               && !string.IsNullOrWhiteSpace(actorScope);
    }

    private static Abstractions.Results.Result<SelfServiceProfileResponse> Unauthorized() =>
        Abstractions.Results.Result<SelfServiceProfileResponse>.Failure(new Abstractions.Results.Error(
            Code: IdentityErrorCodes.SessionNotActive,
            Message: "The current session is no longer active.",
            Type: Abstractions.Results.ErrorType.Unauthorized));
}
