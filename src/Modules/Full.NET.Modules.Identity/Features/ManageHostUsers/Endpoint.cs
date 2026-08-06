using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.FieldProjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/users")
            .WithTags("Identity");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            HostUserQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetSubject(httpContext.User, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await queries.ListAsync(
                    actorUserId,
                    page ?? 1,
                    pageSize ?? 20,
                    includeProfile: true,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<PagedResult<HostUserResponse>>(StatusCodes.Status200OK)
        .RequireOpenAccessAuthentication(IdentityUserManagementPermissions.Read)
        .RequireRateLimiting(IdentityModule.SignatureAuthenticationRateLimitPolicy);

        group.MapGet("/export", async (
            HostUserQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetSubject(httpContext.User, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await queries.ExportAsync(
                    actorUserId,
                    includeProfile: true,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<IReadOnlyList<HostUserResponse>>(StatusCodes.Status200OK)
        .RequireOpenAccessAuthentication(IdentityUserManagementPermissions.Export);

        group.MapGet("/{userId:guid}", async (
            Guid userId,
            HostUserQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetSubject(httpContext.User, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await queries.GetByIdAsync(
                    actorUserId,
                    userId,
                    includeProfile: true,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostUserResponse>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityUserManagementPermissions.Read);

        group.MapPost("/", async (
            CreateHostUserRequest request,
            HostUserManagementService service,
            IUserFieldProjectionResolver projectionResolver,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var allowedProfileFieldKeys = Array.Empty<string>();
            if (request.Profile is not null)
            {
                if (!TryGetSubject(httpContext.User, out var actorUserId))
                {
                    return Results.Unauthorized();
                }

                allowedProfileFieldKeys = await ResolveAllowedProfileFieldKeysAsync(
                        actorUserId,
                        request.Profile,
                        projectionResolver,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (allowedProfileFieldKeys.Length == 0)
                {
                    return Results.Forbid();
                }
            }

            var result = await service.CreateAsync(
                    request,
                    allowedProfileFieldKeys,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/identity/users/{result.Value!.Id:D}",
                result.Value);
        })
        .Produces<HostUserResponse>(StatusCodes.Status201Created)
        .RequireFullNetPermission(IdentityUserManagementPermissions.Create);

        group.MapPut("/{userId:guid}", async (
            Guid userId,
            UpdateHostUserRequest request,
            HostUserManagementService service,
            IUserFieldProjectionResolver projectionResolver,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var allowedProfileFieldKeys = Array.Empty<string>();
            if (request.Profile is not null)
            {
                if (!TryGetSubject(httpContext.User, out var actorUserId))
                {
                    return Results.Unauthorized();
                }

                allowedProfileFieldKeys = await ResolveAllowedProfileFieldKeysAsync(
                        actorUserId,
                        request.Profile,
                        projectionResolver,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (allowedProfileFieldKeys.Length == 0)
                {
                    return Results.Forbid();
                }
            }

            var result = await service.UpdateAsync(
                    userId,
                    request,
                    allowedProfileFieldKeys,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostUserResponse>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityUserManagementPermissions.Update);

        group.MapPost("/{userId:guid}/disable", async (
            Guid userId,
            HostUserManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(userId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostUserResponse>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityUserManagementPermissions.Disable);

        group.MapPost("/{userId:guid}/enable", async (
            Guid userId,
            HostUserManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.EnableAsync(userId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostUserResponse>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityUserManagementPermissions.Enable);

        group.MapPost("/{userId:guid}/reset-password", async (
            Guid userId,
            ResetHostUserPasswordRequest request,
            HostUserManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ResetPasswordAsync(userId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostUserResponse>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityUserManagementPermissions.ResetPassword);

        group.MapGet("/{userId:guid}/roles", async (
            Guid userId,
            HostUserRolesService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(userId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireFullNetPermission(IdentityUserManagementPermissions.Read)
        .Produces<HostUserRolesResponse>(StatusCodes.Status200OK);

        group.MapPut("/{userId:guid}/roles", async (
            Guid userId,
            ReplaceHostUserRolesRequest request,
            HostUserRolesService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReplaceAsync(userId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireFullNetPermission(IdentityUserManagementPermissions.AssignRoles)
        .Produces<HostUserRolesResponse>(StatusCodes.Status200OK);
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

    internal static async Task<string[]> ResolveAllowedProfileFieldKeysAsync(
        Guid actorUserId,
        HostUserProfileWriteRequest profile,
        IUserFieldProjectionResolver projectionResolver,
        CancellationToken cancellationToken)
    {
        var requestedFieldKeys = HostUserProfileMapper.NormalizeFieldKeys(profile.FieldKeys);
        if (requestedFieldKeys.Count == 0)
        {
            return [];
        }

        var projection = await projectionResolver.ResolveAsync(
                actorUserId,
                tenantId: null,
                FieldProjectionResourceKeys.HostUsers,
                cancellationToken)
            .ConfigureAwait(false);
        var allowedFieldKeys = HostUserProfileMapper.GetWritableFieldKeys(projection.FieldKeys);
        var allowed = HostUserProfileMapper.NormalizeFieldKeys(
            requestedFieldKeys,
            allowedFieldKeys);
        return allowed.Count == requestedFieldKeys.Count
            ? allowed.ToArray()
            : [];
    }
}
