using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>MyTodos管理 API 路由注册。</summary>
namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/delegation", async (
            Guid todoId, WorkflowTodoDelegationService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.GetAsync(todoId, actor, token).ConfigureAwait(false), context);
        }).WithName("workflowGetTodoDelegation").WithTags("WorkflowTodos")
            .Produces<WorkflowTodoDelegationStateResponse>().ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRead));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/delegation-candidates", async (
            Guid todoId, int? page, int? pageSize, WorkflowTodoAssigneeCandidateQueryService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.ListDelegationAsync(todoId, actor, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context);
        }).WithName("workflowListTodoDelegationCandidates").WithTags("WorkflowTodos")
            .Produces<WorkflowRecipientCandidatePageResponse>().ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosDelegate));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/delegate", async (
            Guid todoId, DelegateWorkflowTodoRequest request, WorkflowTodoTransferService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            if (!await CanSubmitSignatureAsync(context, request.SignatureFileId).ConfigureAwait(false)) return SignatureForbidden(mapper, context);
            return mapper.Map(await service.DelegateAsync(todoId, actor, request, token).ConfigureAwait(false), context);
        }).WithName("workflowDelegateTodo").WithTags("WorkflowTodos")
            .Produces<WorkflowInstanceResponse>().ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosDelegate));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/delegation/recall", async (
            Guid todoId, RecallWorkflowTodoDelegationRequest request, WorkflowTodoDelegationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.RecallAsync(todoId, actor, request, token).ConfigureAwait(false), context);
        }).WithName("workflowRecallTodoDelegation").WithTags("WorkflowTodos")
            .Produces<WorkflowInstanceResponse>().ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRecallDelegation));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/form-attachments/{fileId:guid}/content", async (
            Guid todoId, Guid fileId, WorkflowFormAttachmentAccessService attachments,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            var scope = WorkflowManagementScope.Resolve(context.RequestServices.GetRequiredService<ICurrentTenant>());
            var result = await attachments.OpenTodoAttachmentAsync(todoId, fileId, actor, scope.TenantScopeKey, token).ConfigureAwait(false);
            if (!result.IsSuccess) return mapper.Map(Result<object?>.Failure(result.Error!), context);
            var file = result.Value!;
            return Results.File(file.Content, file.ContentType, file.OriginalFileName, enableRangeProcessing: true);
        }).WithName("workflowDownloadTodoFormAttachment").WithTags("WorkflowTodos")
            .Produces<Stream>(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRead));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/vote", async (
            Guid todoId, VoteWorkflowTodoRequest request, WorkflowVoteTodoService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            if (request.SignatureFileId is not null && !(await context.RequestServices.GetRequiredService<IAuthorizationService>()
                .AuthorizeAsync(context.User, FullNetPermissionPolicies.For(WorkflowPermissions.TodosSign)).ConfigureAwait(false)).Succeeded)
                return mapper.Map(Result<WorkflowInstanceResponse>.Failure(new Error(WorkflowErrorCodes.TodoActionForbidden,
                    "Signature permission is required.", ErrorType.Forbidden)), context);
            return mapper.Map(await service.VoteAsync(todoId, actor, request, token).ConfigureAwait(false), context);
        }).WithName("workflowVoteTodo").WithTags("WorkflowTodos")
            .Produces<WorkflowInstanceResponse>().ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosVote));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/transfer-candidates", async (
            Guid todoId, int? page, int? pageSize, WorkflowTodoAssigneeCandidateQueryService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.ListTransferAsync(todoId, actor, page ?? 1, pageSize ?? 50, token).ConfigureAwait(false), context);
        }).WithName("workflowListTodoTransferCandidates").WithTags("WorkflowTodos")
            .Produces<WorkflowRecipientCandidatePageResponse>().ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosTransfer));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/transfer", async (
            Guid todoId, TransferWorkflowTodoRequest request, WorkflowTodoTransferService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            if (!await CanSubmitSignatureAsync(context, request.SignatureFileId).ConfigureAwait(false))
                return SignatureForbidden(mapper, context);
            return mapper.Map(await service.TransferAsync(todoId, actor, request, token).ConfigureAwait(false), context);
        }).WithName("workflowTransferTodo").WithTags("WorkflowTodos")
            .Produces<WorkflowInstanceResponse>().ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosTransfer));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/countersign-candidates", async (
            Guid todoId, int? page, int? pageSize, WorkflowTodoAssigneeCandidateQueryService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.ListCountersignAsync(todoId, actor, page ?? 1, pageSize ?? 50, token).ConfigureAwait(false), context);
        }).WithName("workflowListTodoCountersignCandidates").WithTags("WorkflowTodos")
            .Produces<WorkflowRecipientCandidatePageResponse>().ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosCountersign));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/signature-uploads", async (
            Guid todoId, long expectedRevision, IFormFile? file, WorkflowSignatureUploadService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            if (file is null) return mapper.Map(Result<WorkflowSignatureUploadResponse>.Failure(new Error(
                WorkflowErrorCodes.TodoSignatureInvalid, "Signature file is required.", ErrorType.Validation)), context);
            await using var stream = file.OpenReadStream();
            return mapper.Map(await service.UploadAsync(todoId, actor, expectedRevision, file.ContentType,
                stream, file.Length, token).ConfigureAwait(false), context);
        }).WithName("workflowUploadTodoSignature").WithTags("WorkflowTodos")
            .Accepts<IFormFile>("multipart/form-data").Produces<WorkflowSignatureUploadResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(1_114_112))
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute { MultipartBodyLengthLimit = 1_048_576 })
            .DisableAntiforgery().RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosSign));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/signature-evidence", async (
            Guid todoId, WorkflowSignatureReadService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            var scope = WorkflowManagementScope.Resolve(context.RequestServices.GetRequiredService<ICurrentTenant>());
            return mapper.Map(await service.ListAsync(todoId, actor, scope.TenantScopeKey, token).ConfigureAwait(false), context);
        }).WithName("workflowListTodoSignatureEvidence").WithTags("WorkflowTodos")
            .Produces<IReadOnlyList<WorkflowSignatureEvidenceResponse>>().ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRead));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/signature-evidence/{evidenceId:guid}/content", async (
            Guid todoId, Guid evidenceId, WorkflowSignatureReadService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            var scope = WorkflowManagementScope.Resolve(context.RequestServices.GetRequiredService<ICurrentTenant>());
            var result = await service.OpenAsync(todoId, evidenceId, actor, scope.TenantScopeKey, token).ConfigureAwait(false);
            if (!result.IsSuccess) return mapper.Map(Result<object?>.Failure(result.Error!), context);
            return Results.File(result.Value!.Content, result.Value.ContentType, result.Value.OriginalFileName);
        }).WithName("workflowDownloadTodoSignatureEvidence").WithTags("WorkflowTodos")
            .Produces(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRead));

        endpoints.MapGet("/api/v1/workflow/todos/mine", async (
            int? page,
            int? pageSize,
            string? definitionKey,
            string? businessType,
            DateTimeOffset? arrivedFromUtc,
            DateTimeOffset? arrivedToUtc,
            WorkflowTodoQueryService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.ListPendingAsync(
                        actorUserId,
                        page ?? 1,
                        pageSize ?? 20,
                        definitionKey,
                        businessType,
                        arrivedFromUtc,
                        arrivedToUtc,
                        token)
                    .ConfigureAwait(false),
                context);
        })
        .WithName("workflowListMyTodos")
        .WithTags("WorkflowTodos")
        .Produces<PagedResult<WorkflowTodoListItemResponse>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRead));

        endpoints.MapGet("/api/v1/workflow/todos/mine/history", async (
            int? page,
            int? pageSize,
            string? definitionKey,
            string? businessType,
            string? resultActionKey,
            DateTimeOffset? completedFromUtc,
            DateTimeOffset? completedToUtc,
            WorkflowTodoQueryService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.ListHistoryAsync(
                        actorUserId,
                        page ?? 1,
                        pageSize ?? 20,
                        definitionKey,
                        businessType,
                        resultActionKey,
                        completedFromUtc,
                        completedToUtc,
                        token)
                    .ConfigureAwait(false),
                context);
        })
        .WithName("workflowListMyTodoHistory")
        .WithTags("WorkflowTodos")
        .Produces<PagedResult<WorkflowTodoListItemResponse>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRead));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/complete", async (
            Guid todoId, ActWorkflowTodoRequest request, WorkflowTodoManagementService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actor)) return Results.Unauthorized();
            if (!await CanSubmitSignatureAsync(context, request.SignatureFileId).ConfigureAwait(false))
                return SignatureForbidden(mapper, context);
            return mapper.Map(await service.CompleteAsync(todoId, actor, request, token).ConfigureAwait(false), context);
        }).WithName("workflowCompleteTodo").WithTags("WorkflowTodos").Produces<WorkflowInstanceResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosComplete));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}", async (
            Guid todoId,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(await service.GetAsync(todoId, actorUserId, token).ConfigureAwait(false), context);
        })
        .WithName("workflowGetTodo")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowTodoDetailResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRead));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/return-targets", async (
            Guid todoId,
            int? page,
            int? pageSize,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.ListReturnTargetsAsync(
                    todoId, actorUserId, page ?? 1, pageSize ?? 100, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowListTodoReturnTargets")
        .WithTags("WorkflowTodos")
        .Produces<IReadOnlyList<WorkflowTodoReturnTargetResponse>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosReturn));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/countersign-chain", async (
            Guid todoId,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.GetCountersignChainAsync(todoId, actorUserId, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowGetTodoCountersignChain")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowTodoCountersignChainResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosCountersign));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/countersign", async (
            Guid todoId,
            CountersignWorkflowTodoRequest request,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.CountersignAsync(todoId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowCountersignTodo")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosCountersign));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/countersign/cancel", async (
            Guid todoId,
            CancelWorkflowTodoCountersignRequest request,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.CancelCountersignAsync(todoId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowCancelTodoCountersign")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosCountersign));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/runtime", async (
            Guid todoId,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.GetRuntimeAsync(todoId, actorUserId, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowGetTodoRuntime")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowTodoRuntimeResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRead));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/approve", async (
            Guid todoId,
            ActWorkflowTodoRequest request,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            if (!await CanSubmitSignatureAsync(context, request.SignatureFileId).ConfigureAwait(false))
                return SignatureForbidden(mapper, context);
            return mapper.Map(
                await service.ApproveAsync(todoId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowApproveTodo")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosApprove));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/reject", async (
            Guid todoId,
            ActWorkflowTodoRequest request,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            if (!await CanSubmitSignatureAsync(context, request.SignatureFileId).ConfigureAwait(false))
                return SignatureForbidden(mapper, context);
            return mapper.Map(
                await service.RejectAsync(todoId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowRejectTodo")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosReject));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/return", async (
            Guid todoId,
            ReturnWorkflowTodoRequest request,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            if (!await CanSubmitSignatureAsync(context, request.SignatureFileId).ConfigureAwait(false))
                return SignatureForbidden(mapper, context);
            return mapper.Map(
                await service.ReturnAsync(todoId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowReturnTodo")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosReturn));
    }

    /// <summary>动作权限不能隐式授予签名证据权限；即使文件属于本人也必须独立授权。</summary>
    private static async Task<bool> CanSubmitSignatureAsync(HttpContext context, Guid? fileId) =>
        fileId is null || (await context.RequestServices.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(context.User, FullNetPermissionPolicies.For(WorkflowPermissions.TodosSign)).ConfigureAwait(false)).Succeeded;

    private static IResult SignatureForbidden(IApiResultMapper mapper, HttpContext context) =>
        mapper.Map(Result<WorkflowInstanceResponse>.Failure(new Error(WorkflowErrorCodes.TodoActionForbidden,
            "Signature permission is required.", ErrorType.Forbidden)), context);

    private static bool TryGetActor(HttpContext context, out Guid actorUserId) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out actorUserId);
}
