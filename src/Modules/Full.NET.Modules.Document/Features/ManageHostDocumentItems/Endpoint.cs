using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentItems;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/document/host/items")
            .WithTags("DocumentHostItems");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            HostDocumentItemQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(page ?? 1, pageSize ?? 20, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostListItems")
        .Produces<PagedResult<HostDocumentItemResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Read));

        group.MapGet("/{itemId:guid}", async (
            Guid itemId,
            HostDocumentItemQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(itemId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostGetItem")
        .Produces<HostDocumentItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Read));

        group.MapGet("/{itemId:guid}/versions", async (
            Guid itemId,
            HostDocumentItemQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListVersionsAsync(itemId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostListItemVersions")
        .Produces<IReadOnlyList<HostDocumentVersionResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Read));

        group.MapPost("/", async (
            CreateHostDocumentItemRequest request,
            HostDocumentItemManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(userId, request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created($"/api/v1/document/host/items/{result.Value!.Id:D}", result.Value);
        })
        .WithName("documentHostCreateItem")
        .Produces<HostDocumentItemResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Create));

        group.MapPut("/{itemId:guid}", async (
            Guid itemId,
            UpdateHostDocumentItemRequest request,
            HostDocumentItemManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.UpdateAsync(itemId, userId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostUpdateItem")
        .Produces<HostDocumentItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Update));

        group.MapPost("/{itemId:guid}/versions", async (
            Guid itemId,
            AddHostDocumentVersionRequest request,
            HostDocumentItemManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.AddVersionAsync(itemId, userId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostAddItemVersion")
        .Produces<HostDocumentItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.AddVersion));

        group.MapPost("/{itemId:guid}/versions/upload", async (
            Guid itemId,
            IFormFile? file,
            HostDocumentItemManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            if (file is null)
            {
                return mapper.Map(
                    Result<HostDocumentItemResponse>.Failure(new Error(
                        DocumentErrorCodes.Invalid,
                        "Multipart file field is required.",
                        ErrorType.Validation)),
                    httpContext);
            }

            await using var stream = file.OpenReadStream();
            var result = await service.AddVersionFromUploadAsync(
                    itemId,
                    userId,
                    file.FileName,
                    file.ContentType,
                    stream,
                    file.Length,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostUploadItemVersion")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<HostDocumentItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .DisableAntiforgery()
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.AddVersion));

        group.MapGet("/{itemId:guid}/preview", async (
            Guid itemId,
            HostDocumentItemQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.OpenVersionPreviewAsync(itemId, null, cancellationToken)
                .ConfigureAwait(false);
            return MapPreviewResult(result, mapper, httpContext);
        })
        .WithName("documentHostPreviewItemContent")
        .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Read));

        group.MapGet("/{itemId:guid}/versions/{versionId:guid}/preview", async (
            Guid itemId,
            Guid versionId,
            HostDocumentItemQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.OpenVersionPreviewAsync(itemId, versionId, cancellationToken)
                .ConfigureAwait(false);
            return MapPreviewResult(result, mapper, httpContext);
        })
        .WithName("documentHostPreviewItemVersionContent")
        .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Read));

        group.MapGet("/{itemId:guid}/content", async (
            Guid itemId,
            HostDocumentItemQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.OpenCurrentVersionContentAsync(itemId, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(
                    Result<HostDocumentItemResponse>.Failure(result.Error!),
                    httpContext);
            }

            var content = result.Value!;
            return Results.File(
                content.Content,
                content.ContentType,
                content.OriginalFileName,
                enableRangeProcessing: true);
        })
        .WithName("documentHostDownloadItemContent")
        .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Download));

        group.MapPost("/{itemId:guid}/delete", async (
            Guid itemId,
            DeleteHostDocumentItemRequest request,
            HostDocumentItemManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.DeleteAsync(itemId, userId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostDeleteItem")
        .Produces<bool>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Delete));

        group.MapPost("/{itemId:guid}/restore", async (
            Guid itemId,
            RestoreHostDocumentItemRequest request,
            HostDocumentItemManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.RestoreAsync(itemId, userId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostRestoreItem")
        .Produces<HostDocumentItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Restore));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }

    private static IResult MapPreviewResult(
        Result<HostFileContent> result,
        IApiResultMapper mapper,
        HttpContext httpContext)
    {
        if (!result.IsSuccess)
        {
            return mapper.Map(
                Result<HostDocumentItemResponse>.Failure(result.Error!),
                httpContext);
        }

        var content = result.Value!;
        httpContext.Response.Headers.ContentDisposition = "inline";
        return Results.File(
            content.Content,
            content.ContentType,
            enableRangeProcessing: true);
    }
}
