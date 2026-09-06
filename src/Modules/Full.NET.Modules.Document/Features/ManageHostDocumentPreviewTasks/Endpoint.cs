using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;

/// <summary>Host 文档 Office 预览转换任务 HTTP 端点。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/document/host/preview-tasks")
            .WithTags("DocumentHostPreviewTasks");

        group.MapPost("/", async (
            CreateHostDocumentPreviewTaskRequest request,
            HostDocumentPreviewTaskManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service
                .CreateAsync(request, userId, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/document/host/preview-tasks/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("documentHostCreateDocumentPreviewTask")
        .Produces<HostDocumentPreviewTaskResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPreviewTaskPermissions.Create));

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? documentItemId,
            HostDocumentPreviewTaskQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries
                .ListAsync(page ?? 1, pageSize ?? 20, documentItemId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostListDocumentPreviewTasks")
        .Produces<PagedResult<HostDocumentPreviewTaskResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPreviewTaskPermissions.Read));

        group.MapGet("/{taskId:guid}", async (
            Guid taskId,
            HostDocumentPreviewTaskQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostGetDocumentPreviewTask")
        .Produces<HostDocumentPreviewTaskResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPreviewTaskPermissions.Read));

        group.MapGet("/{taskId:guid}/content", async (
            Guid taskId,
            HostDocumentPreviewTaskQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.OpenOutputContentAsync(taskId, cancellationToken).ConfigureAwait(false);
            return MapPreviewResult(result, mapper, httpContext);
        })
        .WithName("documentHostDownloadDocumentPreviewTaskContent")
        .Produces<Stream>(StatusCodes.Status200OK, "application/pdf")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPreviewTaskPermissions.Read));
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
                Result<HostDocumentPreviewTaskResponse>.Failure(result.Error!),
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
