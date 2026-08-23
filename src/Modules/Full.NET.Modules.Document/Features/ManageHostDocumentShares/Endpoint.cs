using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentShares;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/document/host/shares")
            .WithTags("DocumentHostShares");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            HostDocumentShareQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.PageAsync(page ?? 1, pageSize ?? 20, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostListDocumentShares")
        .Produces<PagedResult<HostDocumentShareResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentSharePermissions.Read));

        group.MapPost("/", async (
            CreateHostDocumentShareRequest request,
            HostDocumentShareManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/document/host/shares/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("documentHostCreateDocumentShare")
        .Produces<HostDocumentShareResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentSharePermissions.Create));

        group.MapPost("/{id:guid}/status", async (
            Guid id,
            UpdateHostDocumentShareStatusRequest request,
            HostDocumentShareManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateStatusAsync(id, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostUpdateDocumentShareStatus")
        .Produces<HostDocumentShareResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentSharePermissions.UpdateStatus));

        group.MapGet("/by-code/{shareCode}", (string shareCode, HttpResponse response) =>
        {
            // 中文注释：Task2 Step5 将匿名分享访问从 GET 切换为 POST，避免计数副作用与
            // HTTP 缓存/浏览器预取意外消耗。旧端点稳定返回 405，提示客户端改用 POST /access。
            response.Headers.Allow = "POST";
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
        })
        .WithName("documentHostRejectDocumentShareByCodeGet")
        .Produces(StatusCodes.Status405MethodNotAllowed)
        .AllowAnonymous();

        var publicGroup = endpoints.MapGroup("/api/v1/document/public/shares")
            .WithTags("DocumentPublicShares");

        publicGroup.MapPost("/{shareCode}/access", async (
            string shareCode,
            AccessHostDocumentShareRequest request,
            HostDocumentShareManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.AccessAnonymousAsync(shareCode, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentPublicAccessDocumentShare")
        .Accepts<AccessHostDocumentShareRequest>("application/json")
        .Produces<HostDocumentShareAccessResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(DocumentModule.AnonymousShareAccessRateLimitPolicy)
        .AllowAnonymous();
    }
}
