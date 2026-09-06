using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Reporting.Features.ExecuteDefinitions;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reporting/definitions")
            .WithTags("ReportingExecutions");

        group.MapPost("/{definitionId:guid}/execute", async (
            Guid definitionId,
            ExecuteReportingDefinitionRequest request,
            int? page,
            int? pageSize,
            ReportingDefinitionExecutionService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ExecuteAsync(
                    definitionId,
                    request,
                    page ?? 1,
                    pageSize ?? ReportingExecutionPolicy.DefaultPageSize,
                    httpContext.User,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingExecuteDefinition")
        .Produces<ReportingExecutionPageResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingExecutionPermissions.Run));
    }
}
