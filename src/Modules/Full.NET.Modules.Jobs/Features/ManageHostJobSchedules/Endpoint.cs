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
            .WithTags("Jobs");

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
        .Produces<PagedResult<HostJobScheduleResponse>>(StatusCodes.Status200OK)
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
        .Produces<IReadOnlyList<HostJobScheduleDefinitionOptionResponse>>(
            StatusCodes.Status200OK)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(HostJobPermissions.SchedulesCreate));

        group.MapGet("/cron-preview", async (
            string cronExpression,
            string timeZoneId,
            HostJobScheduleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.PreviewCronAsync(
                    cronExpression,
                    timeZoneId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostJobScheduleCronPreviewResponse>(StatusCodes.Status200OK)
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
        .Produces<HostJobScheduleResponse>(StatusCodes.Status201Created)
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
        .Produces<HostJobScheduleResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(HostJobPermissions.SchedulesUpdate));

        MapStateChange(group, "pause", enable: false);
        MapStateChange(group, "resume", enable: true);
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
        .Produces<HostJobScheduleResponse>(StatusCodes.Status200OK)
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
