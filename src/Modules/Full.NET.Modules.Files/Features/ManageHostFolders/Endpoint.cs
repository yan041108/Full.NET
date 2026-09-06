using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Files.Features.ManageHostFolders;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/files/host-folders")
            .WithTags("FilesHostFolders");

        group.MapGet("/tree", async (
            HostFolderQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetTreeAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("filesGetHostFolderTree")
        .Produces<IReadOnlyList<HostFolderTreeNode>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostFilePermissions.Read));

        group.MapPost("/", async (
            CreateHostFolderRequest request,
            HostFolderManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetSubject(httpContext.User, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(userId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("filesCreateHostFolder")
        .Produces<HostFolderResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostFolderPermissions.Create));

        group.MapPost("/{folderId:guid}/update", async (
            Guid folderId,
            UpdateHostFolderRequest request,
            HostFolderManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetSubject(httpContext.User, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.UpdateAsync(
                    folderId,
                    userId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("filesUpdateHostFolder")
        .Produces<HostFolderResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostFolderPermissions.Update));

        group.MapPost("/{folderId:guid}/delete", async (
            Guid folderId,
            DeleteHostFolderRequest request,
            HostFolderManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetSubject(httpContext.User, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.DeleteAsync(
                    folderId,
                    userId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("filesDeleteHostFolder")
        .Produces<HostFolderResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostFolderPermissions.Delete));
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
