using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;

/// <summary>映射 Host 流水号规则目录与无副作用预览端点。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/serial-numbers/rules")
            .WithTags("SerialNumbers");

        group.MapGet("", async (
            int? page,
            int? pageSize,
            string? name,
            string? key,
            bool? isEnabled,
            string? sortBy,
            string? sortDirection,
            HostSerialRuleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    name,
                    key,
                    isEnabled,
                    sortBy,
                    sortDirection,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<PagedResult<SerialNumberRuleResponse>>(
            StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            SerialNumberRulePermissions.Read));

        group.MapGet("/{ruleId:guid}", async (
            Guid ruleId,
            HostSerialRuleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(ruleId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<SerialNumberRuleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            SerialNumberRulePermissions.Read));

        group.MapPost("", async (
            CreateSerialNumberRuleRequest request,
            HostSerialRuleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(
                    actorUserId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Created(
                    $"/api/v1/serial-numbers/rules/{result.Value!.Id:D}",
                    result.Value)
                : mapper.Map(result, httpContext);
        })
        .Produces<SerialNumberRuleResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            SerialNumberRulePermissions.Create));

        group.MapPut("/{ruleId:guid}", async (
            Guid ruleId,
            UpdateSerialNumberRuleRequest request,
            HostSerialRuleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.UpdateAsync(
                    ruleId,
                    actorUserId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<SerialNumberRuleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            SerialNumberRulePermissions.Update));

        group.MapPost("/{ruleId:guid}/enable", (
            Guid ruleId,
            ChangeSerialNumberRuleStatusRequest request,
            HostSerialRuleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            ChangeStatusAsync(
                ruleId,
                request,
                true,
                service,
                mapper,
                httpContext,
                cancellationToken))
        .Produces<SerialNumberRuleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            SerialNumberRulePermissions.Enable));

        group.MapPost("/{ruleId:guid}/disable", (
            Guid ruleId,
            ChangeSerialNumberRuleStatusRequest request,
            HostSerialRuleService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            ChangeStatusAsync(
                ruleId,
                request,
                false,
                service,
                mapper,
                httpContext,
                cancellationToken))
        .Produces<SerialNumberRuleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            SerialNumberRulePermissions.Disable));

        group.MapPost("/preview", (
            PreviewSerialNumberRequest request,
            SerialNumberPreviewService service,
            IApiResultMapper mapper,
            HttpContext httpContext) =>
            mapper.Map(service.Preview(request), httpContext))
        .Produces<SerialNumberPreviewResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            SerialNumberRulePermissions.Preview));
    }

    private static async Task<IResult> ChangeStatusAsync(
        Guid ruleId,
        ChangeSerialNumberRuleStatusRequest request,
        bool isEnabled,
        HostSerialRuleService service,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryResolveUserId(httpContext, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = await service.SetEnabledAsync(
                ruleId,
                actorUserId,
                request.Version,
                isEnabled,
                cancellationToken)
            .ConfigureAwait(false);
        return mapper.Map(result, httpContext);
    }

    private static bool TryResolveUserId(
        HttpContext httpContext,
        out Guid userId) =>
        Guid.TryParse(httpContext.User.FindFirst("sub")?.Value, out userId);
}
