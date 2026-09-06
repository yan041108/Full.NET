using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.K3Cloud.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.K3Cloud.Features.ManageDocumentSyncs;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/k3cloud/document-syncs")
            .WithTags("K3CloudDocumentSyncs");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            K3CloudDocumentSyncQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries
                .ListAsync(page ?? 1, pageSize ?? 20, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("k3cloudListDocumentSyncs")
        .Produces<PagedResult<K3CloudDocumentSyncResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(K3CloudDocumentSyncPermissions.Read));

        group.MapGet("/{syncId:guid}", async (
            Guid syncId,
            K3CloudDocumentSyncQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(syncId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("k3cloudGetDocumentSync")
        .Produces<K3CloudDocumentSyncResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(K3CloudDocumentSyncPermissions.Read));

        group.MapPost("/", async (
            CreateK3CloudDocumentSyncRequest request,
            K3CloudDocumentSyncService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(userId, request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("k3cloudCreateDocumentSync")
        .Produces<K3CloudDocumentSyncResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(K3CloudDocumentSyncPermissions.Create));

        group.MapPost("/{syncId:guid}/retry", async (
            Guid syncId,
            K3CloudDocumentSyncService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RetryAsync(syncId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("k3cloudRetryDocumentSync")
        .Produces<K3CloudDocumentSyncResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(K3CloudDocumentSyncPermissions.Retry));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
