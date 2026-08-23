using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Jobs.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobSchedules;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/jobs/host-schedules")
            .WithTags("JobsHostJobSchedules");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? jobDefinitionId,
            string? search,
            bool? isEnabled,
            string? triggerKind,
            HostJobScheduleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    jobDefinitionId,
                    search,
                    isEnabled,
                    triggerKind,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("jobsListHostJobSchedules")
        .Produces<PagedResult<HostJobScheduleResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(HostJobPermissions.SchedulesRead));

        group.MapGet("/definition-options", async (
            HostJobScheduleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListDefinitionOptionsAsync(cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("jobsListHostJobScheduleDefinitionOptions")
        .Produces<IReadOnlyList<HostJobScheduleDefinitionOptionResponse>>(
            StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(HostJobPermissions.SchedulesCreate));

        group.MapGet("/cron-preview", async (
            string cronExpression,
            string timeZoneId,
            int? occurrenceCount,
            HostJobScheduleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.PreviewCronAsync(
                    cronExpression,
                    timeZoneId,
                    Math.Clamp(occurrenceCount ?? 5, 1, 10),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("jobsPreviewHostJobScheduleCron")
        .Produces<HostJobScheduleCronPreviewResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(HostJobPermissions.SchedulesCreate));

        group.MapGet("/{scheduleId:guid}", async (
            Guid scheduleId,
            HostJobScheduleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetByIdAsync(
                    scheduleId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostJobScheduleResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(HostJobPermissions.SchedulesRead));

        group.MapPost("/", async (
            CreateHostJobScheduleRequest request,
            HostJobScheduleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(
                    userId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/jobs/host-schedules/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("jobsCreateHostJobSchedule")
        .Produces<HostJobScheduleResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(HostJobPermissions.SchedulesCreate));

        group.MapPut("/{scheduleId:guid}", async (
            Guid scheduleId,
            UpdateHostJobScheduleRequest request,
            HostJobScheduleService service,
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
                    scheduleId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("jobsUpdateHostJobSchedule")
        .Produces<HostJobScheduleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(HostJobPermissions.SchedulesUpdate));

        MapStateChange(group, "pause", enable: false);
        MapStateChange(group, "resume", enable: true);

        // 硬删除任务计划，对应 Admin.NET DeleteJobTrigger；前置校验失败返回 ProblemDetails。
        group.MapPost("/{scheduleId:guid}/delete", async (
            Guid scheduleId,
            ChangeHostJobScheduleStateRequest request,
            HostJobScheduleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(
                    scheduleId,
                    request.Version,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.NoContent();
        })
        .WithName("jobsDeleteHostJobSchedule")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(HostJobPermissions.SchedulesDelete));
    }

    private static void MapStateChange(
        RouteGroupBuilder group,
        string action,
        bool enable)
    {
        group.MapPost($"/{{scheduleId:guid}}/{action}", async (
            Guid scheduleId,
            ChangeHostJobScheduleStateRequest request,
            HostJobScheduleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = enable
                ? await service.ResumeAsync(
                        userId,
                        scheduleId,
                        request,
                        cancellationToken)
                    .ConfigureAwait(false)
                : await service.PauseAsync(
                        userId,
                        scheduleId,
                        request,
                        cancellationToken)
                    .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName(enable ? "jobsResumeHostJobSchedule" : "jobsPauseHostJobSchedule")
        .Produces<HostJobScheduleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(
                enable
                    ? HostJobPermissions.SchedulesResume
                    : HostJobPermissions.SchedulesPause));
    }

    private static bool TryResolveUserId(
        HttpContext httpContext,
        out Guid userId)
    {
        userId = default;
        return Guid.TryParse(
            httpContext.User.FindFirst("sub")?.Value,
            out userId);
    }
}
