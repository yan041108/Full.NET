using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Reporting.Features.ManageDefinitions;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reporting/definitions")
            .WithTags("ReportingDefinitions");

        group.MapGet("/", async (
            Guid? groupId,
            string? nameContains,
            ReportingDefinitionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(groupId, nameContains, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingListDefinitions")
        .Produces<IReadOnlyList<ReportingDefinitionResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionPermissions.Read));

        group.MapGet("/{definitionId:guid}", async (
            Guid definitionId,
            ReportingDefinitionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(definitionId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingGetDefinition")
        .Produces<ReportingDefinitionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionPermissions.Read));

        group.MapPost("/", async (
            CreateReportingDefinitionRequest request,
            ReportingDefinitionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created($"/api/v1/reporting/definitions/{result.Value!.Id:D}", result.Value);
        })
        .WithName("reportingCreateDefinition")
        .Produces<ReportingDefinitionResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionPermissions.Create));

        group.MapPut("/{definitionId:guid}", async (
            Guid definitionId,
            UpdateReportingDefinitionRequest request,
            ReportingDefinitionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(definitionId, request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingUpdateDefinition")
        .Produces<ReportingDefinitionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionPermissions.Update));

        group.MapDelete("/{definitionId:guid}", async (
            Guid definitionId,
            ReportingDefinitionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(definitionId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingDeleteDefinition")
        .Produces<bool>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionPermissions.Delete));

        group.MapPost("/{definitionId:guid}/publish", async (
            Guid definitionId,
            PublishReportingDefinitionRequest request,
            ReportingDefinitionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service.PublishAsync(definitionId, userId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingPublishDefinition")
        .Produces<ReportingDefinitionVersionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionPermissions.Publish));

        group.MapGet("/{definitionId:guid}/versions", async (
            Guid definitionId,
            ReportingDefinitionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListVersionsAsync(definitionId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingListDefinitionVersions")
        .Produces<IReadOnlyList<ReportingDefinitionVersionResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionPermissions.Read));

        group.MapGet("/{definitionId:guid}/versions/{versionNumber:int}", async (
            Guid definitionId,
            int versionNumber,
            ReportingDefinitionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetVersionAsync(definitionId, versionNumber, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingGetDefinitionVersion")
        .Produces<ReportingDefinitionVersionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingDefinitionPermissions.Read));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
