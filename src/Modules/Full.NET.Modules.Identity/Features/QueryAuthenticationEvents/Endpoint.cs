using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.QueryAuthenticationEvents;

/// <summary>平台管理员认证事件只读接口。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/authentication-events")
            .WithTags("IdentityAuthenticationEvents");
        group.MapGet("/", async (
            int? page, int? pageSize, Guid? userId, string? eventType,
            bool? succeeded, DateTimeOffset? fromUtc, DateTimeOffset? toUtc,
            AuthenticationEventQueryService queries, IApiResultMapper mapper,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(page ?? 1, pageSize ?? 20,
                userId, eventType, succeeded, fromUtc, toUtc, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, context);
        })
        .WithName("identityListAuthenticationEvents")
        .Produces<PagedResult<AuthenticationEventResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(AuthenticationEventPermissions.Read);

        group.MapGet("/{id:guid}", async (
            Guid id, AuthenticationEventQueryService queries,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetAsync(id, cancellationToken).ConfigureAwait(false);
            return result is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound,
                    title: "Authentication event not found.",
                    type: "https://httpstatuses.com/404")
                : Results.Ok(result);
        })
        .WithName("identityGetAuthenticationEvent")
        .Produces<AuthenticationEventResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(AuthenticationEventPermissions.Read);

        group.MapGet("/exports", async (
            DateTimeOffset fromUtc, DateTimeOffset toUtc,
            Guid? userId, string? eventType, bool? succeeded,
            AuthenticationEventExportService exports,
            IApiResultMapper mapper, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actorUserId))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authenticated user id is required.");
            }

            var result = await exports.ExportAsync(actorUserId, fromUtc, toUtc,
                userId, eventType, succeeded, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.File(result.Value!, "application/octet-stream",
                    $"authentication-events-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.csv")
                : mapper.Map(result, context);
        })
        .WithName("identityExportAuthenticationEvents")
        .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(AuthenticationEventPermissions.Read),
            FullNetPermissionPolicies.For(AuthenticationEventPermissions.Export));
    }
}
