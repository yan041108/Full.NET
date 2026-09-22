using Full.NET.Hosting.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Document.Features.UpdateHostDocumentVersionRetention;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut(
                "/api/v1/document/host/version-retention",
                async (
                    UpdateHostDocumentVersionRetentionRequest request,
                    DocumentVersionRetentionSettingService service,
                    IApiResultMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await service.UpdateAsync(request, cancellationToken)
                        .ConfigureAwait(false);
                    return mapper.Map(result, httpContext);
                })
            .WithName("documentHostUpdateVersionRetentionSettings")
            .WithTags("DocumentHostSettings")
            .Accepts<UpdateHostDocumentVersionRetentionRequest>("application/json")
            .Produces<HostDocumentVersionRetentionSettingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Update));
    }
}
