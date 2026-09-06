using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Ocr.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ocr.Features.ManageIdCardTasks;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ocr/id-card-tasks")
            .WithTags("OcrIdCardTasks");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            OcrIdCardTaskQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(page ?? 1, pageSize ?? 20, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("ocrListIdCardTasks")
        .Produces<PagedResult<OcrIdCardTaskResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(OcrIdCardTaskPermissions.Read));

        group.MapGet("/{taskId:guid}", async (
            Guid taskId,
            OcrIdCardTaskQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("ocrGetIdCardTask")
        .Produces<OcrIdCardTaskResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(OcrIdCardTaskPermissions.Read));

        group.MapPost("/", async (
            CreateOcrIdCardTaskRequest request,
            OcrIdCardTaskService service,
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
        .WithName("ocrCreateIdCardTask")
        .Produces<OcrIdCardTaskResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(OcrIdCardTaskPermissions.Create));

        group.MapPost("/{taskId:guid}/confirm", async (
            Guid taskId,
            ConfirmOcrIdCardTaskRequest request,
            OcrIdCardTaskService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.ConfirmAsync(taskId, userId, request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("ocrConfirmIdCardTask")
        .Produces<OcrIdCardTaskResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(OcrIdCardTaskPermissions.Confirm));

        group.MapPost("/{taskId:guid}/reject", async (
            Guid taskId,
            ConfirmOcrIdCardTaskRequest request,
            OcrIdCardTaskService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service
                .RejectAsync(taskId, userId, request.Version, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("ocrRejectIdCardTask")
        .Produces<OcrIdCardTaskResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(OcrIdCardTaskPermissions.Reject));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
