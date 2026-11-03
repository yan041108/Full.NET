using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>启动与办理上传各自绑定资源权限，再叠加独立附件上传权限。</summary>
internal static class WorkflowFormAttachmentUploadEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        Configure(endpoints.MapPost("/api/v1/workflow/definition-versions/{versionId:guid}/attachment-uploads", async (
            Guid versionId, string fieldKey, IFormFile? file, WorkflowFormAttachmentUploadService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor) || actor == Guid.Empty) return Results.Unauthorized();
            if (file is null) return Missing(mapper, context);
            await using var stream = file.OpenReadStream();
            return mapper.Map(await service.ForStartAsync(versionId, actor, fieldKey, file.FileName, file.ContentType,
                stream, file.Length, token).ConfigureAwait(false), context);
        }).WithName("workflowUploadStartFormAttachment").WithTags("WorkflowInstances"))
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.InstancesStart), FullNetPermissionPolicies.For(WorkflowPermissions.FormAttachmentsUpload));
        Configure(endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/attachment-uploads", async (
            Guid todoId, long expectedRevision, string fieldKey, string? formKindKey, IFormFile? file,
            WorkflowFormAttachmentUploadService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor) || actor == Guid.Empty) return Results.Unauthorized();
            if (file is null) return Missing(mapper, context);
            await using var stream = file.OpenReadStream();
            return mapper.Map(await service.ForTodoAsync(todoId, actor, expectedRevision, fieldKey, formKindKey ?? "main", file.FileName,
                file.ContentType, stream, file.Length, token).ConfigureAwait(false), context);
        }).WithName("workflowUploadTodoFormAttachment").WithTags("WorkflowTodos"))
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRead), FullNetPermissionPolicies.For(WorkflowPermissions.FormAttachmentsUpload));
    }
    private static RouteHandlerBuilder Configure(RouteHandlerBuilder builder) => builder.Accepts<IFormFile>("multipart/form-data")
        .Produces<WorkflowFormAttachmentUploadResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409)
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(53_477_376))
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute { MultipartBodyLengthLimit = 52_428_800 })
        .DisableAntiforgery();
    private static IResult Missing(IApiResultMapper mapper, HttpContext context) => mapper.Map(Result<WorkflowFormAttachmentUploadResponse>.Failure(
        new(WorkflowErrorCodes.FormAttachmentInvalid, "A workflow attachment is required.", ErrorType.Validation)), context);
}
