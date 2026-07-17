using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.GetCurrentUser;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/me", async (
            ClaimsPrincipal principal,
            IQueryExecutor queryExecutor,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(
                    principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                    out var userId)
                || !Guid.TryParse(
                    principal.FindFirstValue(IdentityClaimTypes.SessionId),
                    out var sessionId))
            {
                return mapper.Map(Unauthorized(), httpContext);
            }

            var actorScope = principal.FindFirstValue(IdentityClaimTypes.ActorScope)
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(actorScope))
            {
                return mapper.Map(Unauthorized(), httpContext);
            }

            var profile = await queryExecutor
                .QuerySingleOrDefaultAsync<IdentityProfileRecord>(
                    IdentitySql.FindProfileByIdentity,
                    new { UserId = userId, ScopeKey = actorScope },
                    cancellationToken)
                .ConfigureAwait(false);
            if (profile is not { IsActive: true })
            {
                return mapper.Map(Unauthorized(), httpContext);
            }

            var tenantId = Guid.TryParse(
                principal.FindFirstValue(IdentityClaimTypes.TenantId),
                out var parsedTenantId)
                ? parsedTenantId
                : (Guid?)null;
            var response = new CurrentUserResponse(
                userId,
                profile.Username,
                profile.DisplayName,
                tenantId,
                actorScope,
                principal.FindFirstValue(IdentityClaimTypes.Scope) ?? string.Empty,
                principal.FindAll(IdentityClaimTypes.Permission)
                    .Select(claim => claim.Value)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(permission => permission, StringComparer.Ordinal)
                    .ToArray(),
                sessionId,
                profile.PreferredLocale,
                profile.ProfileVersion);
            return mapper.Map(Result<CurrentUserResponse>.Success(response), httpContext);
        })
        .WithTags("Identity")
        .RequireAuthorization();
    }

    private static Result<CurrentUserResponse> Unauthorized() =>
        Result<CurrentUserResponse>.Failure(new Error(
            Code: IdentityErrorCodes.SessionNotActive,
            Message: "The current session is no longer active.",
            Type: ErrorType.Unauthorized));
}
