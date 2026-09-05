using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.DataApproval.Features.ManageRequests;

/// <summary>映射 DataApproval 请求管理端点。</summary>
internal static class Endpoint
{
    /// <summary>注册 DataApproval HTTP 路由。</summary>
    /// <param name="endpoints">应用端点构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/data-approvals/requests")
            .WithTags("DataApprovalRequests");

        group.MapGet("", async (
            int? page,
            int? pageSize,
            string? scenarioKey,
            string? statusKey,
            DataApprovalRequestService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    scenarioKey,
                    statusKey,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("dataApprovalsListRequests")
        .Produces<PagedResult<DataApprovalRequestResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(DataApprovalPermissions.Read));

        group.MapGet("/{requestId:guid}", async (
            Guid requestId,
            DataApprovalRequestService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(requestId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("dataApprovalsGetRequest")
        .Produces<DataApprovalRequestResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(DataApprovalPermissions.Read));

        group.MapPost("", async (
            CreateDataApprovalRequestBody request,
            DataApprovalRequestService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(actorUserId, request, cancellationToken)
                .ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Created(
                    $"/api/v1/data-approvals/requests/{result.Value!.Id:D}",
                    result.Value)
                : mapper.Map(result, httpContext);
        })
        .WithName("dataApprovalsCreateRequest")
        .Produces<DataApprovalRequestResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(DataApprovalPermissions.Create));

        group.MapPost("/{requestId:guid}/cancel", async (
            Guid requestId,
            CancelDataApprovalRequestBody request,
            DataApprovalRequestService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CancelAsync(
                    requestId,
                    actorUserId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("dataApprovalsCancelRequest")
        .Produces<DataApprovalRequestResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(DataApprovalPermissions.Cancel));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst("sub")?.Value, out userId);
}
