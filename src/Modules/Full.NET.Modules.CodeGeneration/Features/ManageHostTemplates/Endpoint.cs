using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostTemplates;

/// <summary>
/// 映射 Host 代码生成模板目录的读写端点。
/// </summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/code-generation/templates")
            .WithTags("CodeGeneration");

        group.MapGet("", async (
            int? page,
            int? pageSize,
            CodeGenerationTemplateQueryService queries,
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
        .Produces<PagedResult<CodeGenerationTemplateResponse>>(
            StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationTemplatePermissions.Read));

        group.MapGet("/{templateId:guid}", async (
            Guid templateId,
            CodeGenerationTemplateQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(
                    templateId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<CodeGenerationTemplateResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationTemplatePermissions.Read));

        group.MapPost("", async (
            CreateCodeGenerationTemplateRequest request,
            CodeGenerationTemplateManagementService service,
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
                    $"/api/v1/code-generation/templates/{result.Value!.Id:D}",
                    result.Value)
                : mapper.Map(result, httpContext);
        })
        .Produces<CodeGenerationTemplateResponse>(
            StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationTemplatePermissions.Create));

        group.MapPut("/{templateId:guid}", async (
            Guid templateId,
            UpdateCodeGenerationTemplateRequest request,
            CodeGenerationTemplateManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.UpdateAsync(
                    templateId,
                    actorUserId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<CodeGenerationTemplateResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationTemplatePermissions.Update));

        group.MapPost("/{templateId:guid}/delete", async (
            Guid templateId,
            DeleteCodeGenerationTemplateRequest request,
            CodeGenerationTemplateManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.DeleteAsync(
                    templateId,
                    actorUserId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return result.IsSuccess
                ? Results.NoContent()
                : mapper.Map(result, httpContext);
        })
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationTemplatePermissions.Delete));
    }

    private static bool TryResolveUserId(
        HttpContext httpContext,
        out Guid userId)
    {
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
