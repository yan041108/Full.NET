using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Calendar.Features.ManageMyPersonalSchedules;

internal static class Endpoint
{
    /// <summary>
    /// 注册当前用户个人日程路由组，包含列表、详情、创建、更新、删除与状态切换操作。
    /// </summary>
    /// <remarks>
    /// 个人日程按当前认证用户过滤，所属用户标识来自可信 <c>sub</c> 声明而非请求体；
    /// 跨用户访问统一返回 not found，避免泄露其他用户日程是否存在。
    /// </remarks>
    /// <param name="endpoints">应用程序路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/calendar/my-personal-schedules")
            .WithTags("CalendarMyPersonalSchedules");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? status,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            PersonalScheduleQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await queries.ListAsync(
                    userId,
                    page ?? 1,
                    pageSize ?? 20,
                    status,
                    fromUtc,
                    toUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("calendarListMyPersonalSchedules")
        .Produces<PagedResult<PersonalScheduleResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(CalendarPermissions.Read));

        group.MapGet("/{scheduleId:guid}", async (
            Guid scheduleId,
            PersonalScheduleQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await queries.GetAsync(userId, scheduleId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("calendarGetMyPersonalSchedule")
        .Produces<PersonalScheduleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(CalendarPermissions.Read));

        group.MapPost("/", async (
            CreatePersonalScheduleRequest request,
            PersonalScheduleManagementService service,
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
                $"/api/v1/calendar/my-personal-schedules/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("calendarCreateMyPersonalSchedule")
        .Produces<PersonalScheduleResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(CalendarPermissions.Create));

        group.MapPut("/{scheduleId:guid}", async (
            Guid scheduleId,
            UpdatePersonalScheduleRequest request,
            PersonalScheduleManagementService service,
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
        .WithName("calendarUpdateMyPersonalSchedule")
        .Produces<PersonalScheduleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(CalendarPermissions.Update));

        group.MapPost("/{scheduleId:guid}/delete", async (
            Guid scheduleId,
            ChangePersonalScheduleRequest request,
            PersonalScheduleManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.DeleteAsync(
                    userId,
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
        .WithName("calendarDeleteMyPersonalSchedule")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(CalendarPermissions.Delete));

        group.MapPost("/{scheduleId:guid}/status", async (
            Guid scheduleId,
            SetPersonalScheduleStatusRequest request,
            PersonalScheduleManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.SetStatusAsync(
                    userId,
                    scheduleId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("calendarSetMyPersonalScheduleStatus")
        .Produces<PersonalScheduleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(CalendarPermissions.SetStatus));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
