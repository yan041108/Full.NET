using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Reporting.Features.ManageGroups;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reporting/groups")
            .WithTags("ReportingGroups");

        group.MapGet("/", async (
            ReportingGroupQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingListGroups")
        .Produces<IReadOnlyList<ReportingGroupResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingGroupPermissions.Read));

        group.MapGet("/{groupId:guid}", async (
            Guid groupId,
            ReportingGroupQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(groupId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingGetGroup")
        .Produces<ReportingGroupResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingGroupPermissions.Read));

        group.MapPost("/", async (
            CreateReportingGroupRequest request,
            ReportingGroupManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created($"/api/v1/reporting/groups/{result.Value!.Id:D}", result.Value);
        })
        .WithName("reportingCreateGroup")
        .Produces<ReportingGroupResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingGroupPermissions.Create));

        group.MapPut("/{groupId:guid}", async (
            Guid groupId,
            UpdateReportingGroupRequest request,
            ReportingGroupManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(groupId, request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingUpdateGroup")
        .Produces<ReportingGroupResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingGroupPermissions.Update));

        group.MapDelete("/{groupId:guid}", async (
            Guid groupId,
            ReportingGroupManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(groupId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingDeleteGroup")
        .Produces<bool>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingGroupPermissions.Delete));
    }
}
