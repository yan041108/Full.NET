using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.FieldProjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

internal static class Endpoint
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/users")
            .WithTags("IdentityHostUsers");

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
        .WithName("identityListHostUsers")
        .Produces<PagedResult<HostUserResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
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
        .WithName("identityExportHostUsers")
        .Produces<IReadOnlyList<HostUserResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireOpenAccessAuthentication(IdentityUserManagementPermissions.Export);

        group.MapGet("/export-file", async (
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
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.File(
                HostUserWorkbookCodec.Export(result.Value!),
                WorkbookContentType,
                "host-users.xlsx");
        })
        .WithName("identityExportHostUsersWorkbook")
        .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireOpenAccessAuthentication(IdentityUserManagementPermissions.Export);

        group.MapGet("/import-template", () => Results.File(
                HostUserWorkbookCodec.CreateImportTemplate(),
                WorkbookContentType,
                "host-users-import-template.xlsx"))
            .WithName("identityDownloadHostUserImportTemplate")
            .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireFullNetPermission(IdentityUserManagementPermissions.Import);

        group.MapPost("/import", async (
            ImportHostUsersRequest request,
            HostUserManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ImportAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityImportHostUsers")
        .Produces<ImportHostUsersResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityUserManagementPermissions.Import);

        group.MapPost("/import-file", async (
            [FromForm] IFormFile file,
            HostUserManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return InvalidWorkbook(mapper, httpContext);
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var users = await HostUserWorkbookCodec.ParseImportAsync(
                        stream,
                        file.Length,
                        cancellationToken)
                    .ConfigureAwait(false);
                var result = await service.ImportAsync(
                        new ImportHostUsersRequest(users),
                        cancellationToken)
                    .ConfigureAwait(false);
                return mapper.Map(result, httpContext);
            }
            catch (InvalidDataException)
            {
                return InvalidWorkbook(mapper, httpContext);
            }
        })
        .WithName("identityImportHostUsersWorkbook")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<ImportHostUsersResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .DisableAntiforgery()
        .RequireFullNetPermission(IdentityUserManagementPermissions.Import);

        group.MapPost("/batch-disable", async (
            BatchHostUserIdsRequest request,
            HostUserManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.BatchDisableAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityBatchDisableHostUsers")
        .Produces<BatchHostUserStatusResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityUserManagementPermissions.Disable);

        group.MapPost("/batch-enable", async (
            BatchHostUserIdsRequest request,
            HostUserManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.BatchEnableAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityBatchEnableHostUsers")
        .Produces<BatchHostUserStatusResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityUserManagementPermissions.Enable);

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
        .WithName("identityGetHostUser")
        .Produces<HostUserResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
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
        .WithName("identityCreateHostUser")
        .Produces<HostUserResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
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
        .WithName("identityUpdateHostUser")
        .Produces<HostUserResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
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
        .WithName("identityDisableHostUser")
        .Produces<HostUserResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
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
        .WithName("identityEnableHostUser")
        .Produces<HostUserResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
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
        .WithName("identityResetHostUserPassword")
        .Produces<HostUserResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
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
        .WithName("identityGetHostUserRoles")
        .Produces<HostUserRolesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityUserManagementPermissions.Read);

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
        .WithName("identityReplaceHostUserRoles")
        .Produces<HostUserRolesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityUserManagementPermissions.AssignRoles);
    }

    private static IResult InvalidWorkbook(
        IApiResultMapper mapper,
        HttpContext httpContext) =>
        mapper.Map(
            Result<object?>.Failure(new Error(
                IdentityErrorCodes.UserImportWorkbookInvalid,
                "The user import workbook is invalid.",
                ErrorType.Validation)),
            httpContext);

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
