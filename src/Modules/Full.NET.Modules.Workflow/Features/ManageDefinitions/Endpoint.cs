using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/workflow/definitions").WithTags("WorkflowDefinitions");

        NodeTypeCatalogEndpoint.Map(group);

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

    private static bool TryGetActor(HttpContext context, out Guid actorUserId) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out actorUserId);
}
