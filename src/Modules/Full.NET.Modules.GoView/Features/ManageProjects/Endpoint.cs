using Full.NET.Hosting.Api;
using Full.NET.Modules.GoView.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.GoView.Features.ManageProjects;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/goview/projects")
            .WithTags("GoViewProjects");

        group.MapGet("/", async (
            string? nameContains,
            GoViewProjectQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(nameContains, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("goviewListProjects")
        .Produces<IReadOnlyList<GoViewProjectResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(GoViewProjectPermissions.Read));

        group.MapGet("/{projectId:guid}", async (
            Guid projectId,
            GoViewProjectQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("goviewGetProject")
        .Produces<GoViewProjectResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(GoViewProjectPermissions.Read));

        group.MapPost("/", async (
            CreateGoViewProjectRequest request,
            GoViewProjectManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created($"/api/v1/goview/projects/{result.Value!.Id:D}", result.Value);
        })
        .WithName("goviewCreateProject")
        .Produces<GoViewProjectResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(GoViewProjectPermissions.Create));

        group.MapPut("/{projectId:guid}", async (
            Guid projectId,
            UpdateGoViewProjectRequest request,
            GoViewProjectManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(projectId, request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("goviewUpdateProject")
        .Produces<GoViewProjectResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(GoViewProjectPermissions.Update));

        group.MapPost("/{projectId:guid}/publish", async (
            Guid projectId,
            PublishGoViewProjectRequest request,
            GoViewProjectManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service
                .PublishAsync(projectId, userId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("goviewPublishProject")
        .Produces<GoViewProjectVersionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(GoViewProjectPermissions.Publish));

        group.MapGet("/{projectId:guid}/versions", async (
            Guid projectId,
            GoViewProjectQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListVersionsAsync(projectId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("goviewListProjectVersions")
        .Produces<IReadOnlyList<GoViewProjectVersionResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(GoViewProjectPermissions.Read));

        group.MapGet("/{projectId:guid}/versions/{versionNumber:int}", async (
            Guid projectId,
            int versionNumber,
            GoViewProjectQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetVersionAsync(projectId, versionNumber, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("goviewGetProjectVersion")
        .Produces<GoViewProjectVersionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(GoViewProjectPermissions.Read));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
