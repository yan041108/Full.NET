using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Jobs.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobDefinitions;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/jobs/host-definitions")
            .WithTags("Jobs");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            HostJobDefinitionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<PagedResult<HostJobDefinitionResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostJobPermissions.DefinitionsRead));

        group.MapGet("/{definitionId:guid}", async (
            Guid definitionId,
            HostJobDefinitionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(definitionId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostJobDefinitionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostJobPermissions.DefinitionsRead));

        group.MapPost("/", async (
            CreateHostJobDefinitionRequest request,
            HostJobDefinitionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(userId, request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/jobs/host-definitions/{result.Value!.Id:D}",
                result.Value);
        })
        .Produces<HostJobDefinitionResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostJobPermissions.DefinitionsCreate));

        group.MapPut("/{definitionId:guid}", async (
            Guid definitionId,
            UpdateHostJobDefinitionRequest request,
            HostJobDefinitionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.UpdateAsync(
                    userId,
                    definitionId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostJobDefinitionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostJobPermissions.DefinitionsUpdate));

        group.MapPost("/{definitionId:guid}/disable", async (
            Guid definitionId,
            DisableHostJobDefinitionRequest request,
            HostJobDefinitionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.DisableAsync(
                    userId,
                    definitionId,
                    request.Version,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostJobDefinitionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostJobPermissions.DefinitionsDisable));

        group.MapPost("/{definitionId:guid}/trigger", async (
            Guid definitionId,
            Features.ManageHostJobExecutions.HostJobTriggerService trigger,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await trigger.TriggerAsync(definitionId, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/jobs/host-executions?jobDefinitionId={definitionId:D}",
                result.Value);
        })
        .Produces<HostJobExecutionResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostJobPermissions.DefinitionsTrigger));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
