using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Jobs.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobExecutions;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/jobs/host-executions")
            .WithTags("JobsHostJobExecutions");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? jobDefinitionId,
            Guid? jobScheduleId,
            string? status,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            HostJobExecutionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    jobDefinitionId,
                    jobScheduleId,
                    status,
                    fromUtc,
                    toUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("jobsListHostJobExecutions")
        .Produces<PagedResult<HostJobExecutionResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostJobPermissions.ExecutionsRead));

        group.MapGet("/{executionId:guid}", async (
            Guid executionId,
            HostJobExecutionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(executionId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("jobsGetHostJobExecution")
        .Produces<HostJobExecutionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostJobPermissions.ExecutionsRead));

        // 清空指定作业定义的终态执行记录，对应 Admin.NET ClearJobTriggerRecord。
        group.MapPost("/clear", async (
            Guid jobDefinitionId,
            HostJobExecutionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ClearAsync(jobDefinitionId, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.NoContent();
        })
        .WithName("jobsClearHostJobExecutions")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(HostJobPermissions.ExecutionsClear));
    }
}
