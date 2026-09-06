using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/identity/session-policy", (
            IdentitySessionPolicyQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext) =>
        {
            var result = queries.GetPolicy();
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetHostSessionPolicy")
        .WithTags("IdentityHostOnlineSessions")
        .Produces<IdentitySessionPolicyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentitySessionManagementPermissions.Read);

        var group = endpoints.MapGroup("/api/v1/identity/online-sessions")
            .WithTags("IdentityHostOnlineSessions");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? usernameContains,
            Guid? userId,
            HostOnlineSessionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    usernameContains,
                    userId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListHostOnlineSessions")
        .Produces<PagedResult<HostOnlineSessionResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentitySessionManagementPermissions.Read);

        group.MapPost("/users/{userId:guid}/revoke-all", async (
            Guid userId,
            HostOnlineSessionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetSubject(httpContext.User, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.RevokeAllByUserAsync(
                    actorUserId,
                    userId,
                    httpContext.Connection.RemoteIpAddress?.ToString(),
                    httpContext.Request.Headers.UserAgent.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityRevokeAllHostUserOnlineSessions")
        .Produces<RevokeAllHostUserSessionsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentitySessionManagementPermissions.Revoke);

        group.MapPost("/{sessionId:guid}/revoke", async (
            Guid sessionId,
            HostOnlineSessionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetSubject(httpContext.User, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.RevokeAsync(
                    actorUserId,
                    sessionId,
                    httpContext.Connection.RemoteIpAddress?.ToString(),
                    httpContext.Request.Headers.UserAgent.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityRevokeHostOnlineSession")
        .Produces<HostOnlineSessionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentitySessionManagementPermissions.Revoke);
    }

    private static bool TryGetSubject(
        System.Security.Claims.ClaimsPrincipal principal,
        out Guid userId)
    {
        userId = Guid.Empty;
        var subjects = principal.FindAll(JwtRegisteredClaimNames.Sub).ToArray();
        return subjects.Length == 1
            && Guid.TryParse(subjects[0].Value, out userId)
            && userId != Guid.Empty;
    }
}
