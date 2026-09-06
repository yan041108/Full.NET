using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageRegistrationWays;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/registration-ways")
            .WithTags("IdentityRegistrationWays");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? tenantId,
            string? nameContains,
            bool? isEnabled,
            RegistrationWayQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    tenantId,
                    nameContains,
                    isEnabled,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListRegistrationWays")
        .Produces<PagedResult<RegistrationWayResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityRegistrationWayPermissions.Read);

        group.MapGet("/{wayId:guid}", async (
            Guid wayId,
            RegistrationWayQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(wayId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetRegistrationWay")
        .Produces<RegistrationWayResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityRegistrationWayPermissions.Read);

        group.MapPost("/", async (
            CreateRegistrationWayRequest request,
            RegistrationWayManagementService service,
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
                $"/api/v1/identity/registration-ways/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("identityCreateRegistrationWay")
        .Produces<RegistrationWayResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityRegistrationWayPermissions.Create);

        group.MapPut("/{wayId:guid}", async (
            Guid wayId,
            UpdateRegistrationWayRequest request,
            RegistrationWayManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(wayId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityUpdateRegistrationWay")
        .Produces<RegistrationWayResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireFullNetPermission(IdentityRegistrationWayPermissions.Update);

        group.MapDelete("/{wayId:guid}", async (
            Guid wayId,
            RegistrationWayManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(wayId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityDeleteRegistrationWay")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityRegistrationWayPermissions.Delete);
    }
}
