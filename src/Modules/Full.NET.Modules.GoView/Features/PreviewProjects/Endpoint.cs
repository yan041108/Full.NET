using Full.NET.Hosting.Api;
using Full.NET.Modules.GoView.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.GoView.Features.PreviewProjects;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/goview/projects")
            .WithTags("GoViewPreviews");

        group.MapPost("/{projectId:guid}/preview", async (
            Guid projectId,
            PreviewGoViewProjectRequest request,
            GoViewProjectPreviewService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service
                .PreviewAsync(projectId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("goviewPreviewProject")
        .Produces<GoViewProjectPreviewResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(GoViewProjectPermissions.Preview));
    }
}
