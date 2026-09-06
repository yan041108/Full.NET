using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Reporting.Features.BrowseQueryPorts;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reporting/query-ports")
            .WithTags("ReportingQueryPorts");

        group.MapGet("/", (
            ReportingQueryPortQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext) =>
            mapper.Map(queries.ListAsync(), httpContext))
        .WithName("reportingListQueryPorts")
        .Produces<IReadOnlyList<ReportingQueryPortDefinition>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingQueryPortPermissions.Read));

        group.MapGet("/{queryPortKey}", (
            string queryPortKey,
            ReportingQueryPortQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext) =>
            mapper.Map(queries.GetByKeyAsync(queryPortKey), httpContext))
        .WithName("reportingGetQueryPort")
        .Produces<ReportingQueryPortDefinition>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingQueryPortPermissions.Read));
    }
}
