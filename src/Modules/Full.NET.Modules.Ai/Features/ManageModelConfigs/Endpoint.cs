using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageModelConfigs;

/// <summary>AI 模型配置 CRUD 与连通性测试 HTTP 端点。</summary>
internal static class Endpoint
{
    /// <summary>注册模型配置管理路由。</summary>
    /// <param name="endpoints">路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/model-configs")
            .WithTags("AiModelConfigs");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? tenantId,
            string? nameContains,
            bool? isEnabled,
            AiModelConfigQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    tenantId,
                    nameContains,
                    isEnabled,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiListModelConfigs")
        .Produces<PagedResult<AiModelConfigListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiModelPermissions.Read));

        group.MapGet("/{modelConfigId:guid}", async (
            Guid modelConfigId,
            AiModelConfigQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(modelConfigId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiGetModelConfig")
        .Produces<AiModelConfigResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiModelPermissions.Read));

        group.MapPost("/", async (
            CreateAiModelConfigRequest request,
            AiModelConfigManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/ai/model-configs/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("aiCreateModelConfig")
        .Produces<AiModelConfigResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiModelPermissions.Create));

        group.MapPut("/{modelConfigId:guid}", async (
            Guid modelConfigId,
            UpdateAiModelConfigRequest request,
            AiModelConfigManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(modelConfigId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiUpdateModelConfig")
        .Produces<AiModelConfigResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiModelPermissions.Update));

        group.MapPost("/{modelConfigId:guid}/disable", async (
            Guid modelConfigId,
            AiModelConfigManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(modelConfigId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiDisableModelConfig")
        .Produces<AiModelConfigResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiModelPermissions.Update));

        group.MapPost("/{modelConfigId:guid}/test", async (
            Guid modelConfigId,
            AiModelConfigOperationsService operations,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await operations.TestConnectivityAsync(modelConfigId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiTestModelConfig")
        .Produces<TestAiModelConfigResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiModelPermissions.Test));
    }
}
