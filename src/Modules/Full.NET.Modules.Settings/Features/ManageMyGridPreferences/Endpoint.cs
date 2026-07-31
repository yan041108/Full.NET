using System.Security.Claims;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Settings.Features.ManageMyGridPreferences;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/me/grid-preferences")
            .WithTags("Settings")
            .RequireAuthorization();

        group.MapGet("/{gridKey}", async (
            string gridKey,
            ClaimsPrincipal principal,
            MyGridPreferenceService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.GetAsync(userId, gridKey, cancellationToken)
                    .ConfigureAwait(false),
                httpContext);
        })
        .Produces<GridPreferenceResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{gridKey}", async (
            string gridKey,
            UpdateGridPreferenceRequest request,
            ClaimsPrincipal principal,
            MyGridPreferenceService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.PutAsync(userId, gridKey, request, cancellationToken)
                    .ConfigureAwait(false),
                httpContext);
        })
        .Produces<GridPreferenceResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{gridKey}", async (
            string gridKey,
            ClaimsPrincipal principal,
            MyGridPreferenceService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.DeleteAsync(userId, gridKey, cancellationToken)
                    .ConfigureAwait(false),
                httpContext);
        })
        .Produces<GridPreferenceResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static bool TryResolveUserId(
        ClaimsPrincipal principal,
        out Guid userId) =>
        Guid.TryParse(
            principal.FindFirstValue("sub"),
            out userId);
}
