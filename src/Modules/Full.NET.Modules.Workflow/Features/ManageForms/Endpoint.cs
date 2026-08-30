using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageForms;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/workflow/forms")
            .WithTags("WorkflowForms");

        FormComponentCatalogEndpoint.Map(group);

        group.MapGet("/", async (WorkflowFormManagementService service, IApiResultMapper mapper,
            HttpContext context, CancellationToken token) =>
            mapper.Map(await service.ListAsync(token).ConfigureAwait(false), context))
            .WithName("workflowListForms")
            .Produces<IReadOnlyList<WorkflowFormResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.FormsRead));

        group.MapGet("/{formId:guid}", async (Guid formId, WorkflowFormManagementService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.GetAsync(formId, token).ConfigureAwait(false), context))
            .WithName("workflowGetForm")
            .Produces<WorkflowFormResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.FormsRead));

        group.MapPost("/", async (CreateWorkflowFormRequest request,
            WorkflowFormManagementService service, IApiResultMapper mapper,
            HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(actorUserId, request, token).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Created($"/api/v1/workflow/forms/{result.Value!.Id:D}", result.Value)
                : mapper.Map(result, context);
        })
        .WithName("workflowCreateForm")
        .Produces<WorkflowFormResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.FormsCreate));

        group.MapPut("/{formId:guid}/draft", async (Guid formId,
            UpdateWorkflowFormDraftRequest request, WorkflowFormManagementService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.UpdateDraftAsync(formId, request, token).ConfigureAwait(false), context))
            .WithName("workflowUpdateFormDraft")
            .Produces<WorkflowFormResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.FormsUpdate));

        group.MapPost("/{formId:guid}/publish", async (Guid formId,
            PublishWorkflowFormRequest request, WorkflowFormManagementService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.PublishAsync(formId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowPublishForm")
        .Produces<WorkflowFormVersionResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.FormsPublish));
    }

    public static void MapVersion(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/workflow/form-versions/{versionId:guid}", async (
            Guid versionId, WorkflowFormManagementService service, IApiResultMapper mapper,
            HttpContext context, CancellationToken token) =>
            mapper.Map(await service.GetVersionAsync(versionId, token).ConfigureAwait(false), context))
            .WithName("workflowGetFormVersion")
            .WithTags("WorkflowForms")
            .Produces<WorkflowFormVersionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.FormsRead));
    }

    private static bool TryGetActor(HttpContext context, out Guid actorUserId) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out actorUserId);
}
