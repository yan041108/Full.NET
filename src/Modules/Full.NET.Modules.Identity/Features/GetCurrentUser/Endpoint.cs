using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Identity.Authorization;
using global::Dapper;
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
            PermissionClaimEvaluator permissionClaimEvaluator,
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

            var profileParameters = new DynamicParameters();
            profileParameters.Add("UserId", userId);
            profileParameters.Add("ScopeKey", actorScope);
            var profile = await queryExecutor
                .QuerySingleOrDefaultAsync<IdentityProfileRecord>(
                    IdentitySql.FindProfileByIdentity,
                    profileParameters,
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
            var scope = principal.FindFirstValue(IdentityClaimTypes.Scope) ?? string.Empty;
            var isSuperAdministrator =
                PermissionClaimEvaluator.IsSuperAdministrator(principal);
            var permissions = permissionClaimEvaluator.ResolvePermissions(principal);
            var response = new CurrentUserResponse(
                userId,
                profile.Username,
                profile.DisplayName,
                tenantId,
                actorScope,
                scope,
                isSuperAdministrator,
                permissions,
                sessionId,
                profile.PreferredLocale,
                profile.ProfileVersion);
            return mapper.Map(Result<CurrentUserResponse>.Success(response), httpContext);
        })
        .WithName("identityGetCurrentUser")
        .WithTags("IdentityMe")
        .Produces<CurrentUserResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .RequireAuthorization();
    }

    private static Result<CurrentUserResponse> Unauthorized() =>
        Result<CurrentUserResponse>.Failure(new Error(
            Code: IdentityErrorCodes.SessionNotActive,
            Message: "The current session is no longer active.",
            Type: ErrorType.Unauthorized));
}
