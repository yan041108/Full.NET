using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

/// <summary>映射工作流定义、版本、节点目录和抄送候选人接口。</summary>
internal static class Endpoint
{
    /// <summary>映射工作流定义管理接口。</summary>
    /// <param name="endpoints">Endpoint 路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/workflow/definitions").WithTags("WorkflowDefinitions");

        NodeTypeCatalogEndpoint.Map(group);

        group.MapGet("/recipient-candidates", async (
            int? page,
            int? pageSize,
            [FromServices] IHostUserSelectionDirectory directory,
            CancellationToken token) =>
        {
            var result = await directory.ListActiveHostUsersAsync(
                Math.Max(page ?? 1, 1),
                Math.Clamp(pageSize ?? 50, 1, 100),
                token).ConfigureAwait(false);
            return new WorkflowRecipientCandidatePageResponse(
                result.Items.Select(item => new WorkflowRecipientCandidateResponse(
                    item.Id,
                    item.Username,
                    item.DisplayName)).ToArray(),
                result.Page,
                result.PageSize,
                result.Total);
        })
        .WithName("workflowListRecipientCandidates")
        .Produces<WorkflowRecipientCandidatePageResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsRead));

        group.MapGet("/", async (WorkflowDefinitionManagementService service, IApiResultMapper mapper,
            HttpContext context, CancellationToken token) => mapper.Map(await service.ListAsync(token).ConfigureAwait(false), context))
            .WithName("workflowListDefinitions")
            .Produces<IReadOnlyList<WorkflowDefinitionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsRead));

        group.MapGet("/{definitionId:guid}", async (Guid definitionId,
            WorkflowDefinitionManagementService service, IApiResultMapper mapper,
            HttpContext context, CancellationToken token) => mapper.Map(await service.GetAsync(definitionId, token).ConfigureAwait(false), context))
            .WithName("workflowGetDefinition")
            .Produces<WorkflowDefinitionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsRead));

        group.MapPost("/", async (CreateWorkflowDefinitionRequest request,
            WorkflowDefinitionManagementService service, IApiResultMapper mapper,
            HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            var result = await service.CreateAsync(actor, request, token).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Created($"/api/v1/workflow/definitions/{result.Value!.Id:D}", result.Value)
                : mapper.Map(result, context);
        })
        .WithName("workflowCreateDefinition")
        .Produces<WorkflowDefinitionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsCreate));

        group.MapPut("/{definitionId:guid}/draft", async (Guid definitionId,
            UpdateWorkflowDefinitionDraftRequest request, WorkflowDefinitionManagementService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.UpdateDraftAsync(definitionId, actor, request, token).ConfigureAwait(false), context);
        })
        .WithName("workflowUpdateDefinitionDraft")
        .Produces<WorkflowDefinitionResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsUpdate));

        group.MapPost("/{definitionId:guid}/publish", async (Guid definitionId,
            PublishWorkflowDefinitionRequest request, WorkflowDefinitionManagementService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.PublishAsync(definitionId, actor, request, token).ConfigureAwait(false), context);
        })
        .WithName("workflowPublishDefinition")
        .Produces<WorkflowDefinitionVersionResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsPublish));

        group.MapGet("/{definitionId:guid}/versions", async (Guid definitionId,
            WorkflowDefinitionManagementService service, IApiResultMapper mapper,
            HttpContext context, CancellationToken token) => mapper.Map(await service.ListVersionsAsync(definitionId, token).ConfigureAwait(false), context))
            .WithName("workflowListDefinitionVersions")
            .Produces<IReadOnlyList<WorkflowDefinitionVersionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsRead));
    }

    /// <summary>映射不可变工作流定义版本读取接口。</summary>
    /// <param name="endpoints">Endpoint 路由构建器。</param>
    public static void MapVersion(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/workflow/definition-versions/{versionId:guid}", async (
            Guid versionId, WorkflowDefinitionManagementService service, IApiResultMapper mapper,
            HttpContext context, CancellationToken token) => mapper.Map(await service.GetVersionAsync(versionId, token).ConfigureAwait(false), context))
            .WithName("workflowGetDefinitionVersion")
            .WithTags("WorkflowDefinitions")
            .Produces<WorkflowDefinitionVersionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsRead));
    }

    /// <summary>从可信身份声明读取当前操作人。</summary>
    /// <param name="context">当前 HTTP 上下文。</param>
    /// <param name="actorUserId">解析后的操作人标识。</param>
    /// <returns>声明包含有效用户标识时返回 <see langword="true"/>。</returns>
    private static bool TryGetActor(HttpContext context, out Guid actorUserId) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out actorUserId);
}
