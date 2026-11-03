using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IO;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 映射 Host 代码生成预览、Apply、Rollback 与只读运行目录端点。
/// </summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/code-generation/runs")
            .WithTags("CodeGenerationRuns");

        group.MapGet("/workspace", async (CodeGenerationWorkspaceService service, IApiResultMapper mapper,
            HttpContext context, CancellationToken token) => mapper.Map(await service.GetAsync(token).ConfigureAwait(false), context))
            .WithName("codeGenerationGetWorkspaceState")
            .Produces<CodeGenerationWorkspaceStateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequireAuthorization(FullNetPermissionPolicies.For(CodeGenerationRunPermissions.Read));

        group.MapPost("/workspace/recover", async (CodeGenerationWorkspaceRecoveryRequest request, CodeGenerationWorkspaceService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryResolveUserId(context, out _)) return Results.Unauthorized();
            return mapper.Map(await service.RecoverAsync(request, token).ConfigureAwait(false), context);
        })
            .WithName("codeGenerationRecoverWorkspace")
            .Produces<CodeGenerationWorkspaceStateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequireAuthorization(FullNetPermissionPolicies.For(CodeGenerationRunPermissions.Apply), FullNetPermissionPolicies.For(CodeGenerationRunPermissions.Rollback));

        group.MapPost("/preview", async (
            CodeGenerationRunPreviewRequest request,
            CodeGenerationRunService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.PreviewAsync(
                    actorUserId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("codeGenerationPreviewRun")
        .Produces<CodeGenerationRunPreviewResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationRunPermissions.Execute));

        group.MapPost("/apply-plan", async (
            CodeGenerationRunApplyPlanRequest request,
            CodeGenerationApplyService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var actorUserId)) return Results.Unauthorized();
            return mapper.Map(await service.PrepareAsync(actorUserId, request, cancellationToken).ConfigureAwait(false), httpContext);
        })
        .WithName("codeGenerationPrepareApplyPlan")
        .Produces<CodeGenerationRunApplyPlanResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .RequireAuthorization(FullNetPermissionPolicies.For(CodeGenerationRunPermissions.Apply));

        group.MapPost("/apply", async (
            CodeGenerationRunApplyRequest request,
            CodeGenerationApplyService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.ApplyAsync(
                    actorUserId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("codeGenerationApplyRun")
        .Produces<CodeGenerationRunApplyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationRunPermissions.Apply));

        group.MapPost("/rollback", async (
            CodeGenerationRunRollbackRequest request,
            CodeGenerationRollbackService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.RollbackAsync(
                    actorUserId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("codeGenerationRollbackRun")
        .Produces<CodeGenerationRunRollbackResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationRunPermissions.Rollback));

        group.MapPost("/rollback-chain", async (
            CodeGenerationRunRollbackChainRequest request,
            CodeGenerationRollbackService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.RollbackChainAsync(
                    actorUserId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("codeGenerationRollbackRunChain")
        .Produces<CodeGenerationRunRollbackChainResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationRunPermissions.Rollback));

        group.MapGet("", async (
            int? page,
            int? pageSize,
            string? status,
            CodeGenerationRunQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    status,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("codeGenerationListRuns")
        .Produces<PagedResult<CodeGenerationRunResponse>>(
            StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationRunPermissions.Read));

        group.MapGet("/{runId:guid}", async (
            Guid runId,
            CodeGenerationRunQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(
                    runId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("codeGenerationGetRun")
        .Produces<CodeGenerationRunResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationRunPermissions.Read));

        group.MapPost("/{runId:guid}/publication/retry", async (
            Guid runId,
            CodeGenerationPublicationRetryService publications,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out _)) return Results.Unauthorized();
            return mapper.Map(await publications.RetryAsync(runId, cancellationToken).ConfigureAwait(false), httpContext);
        })
        .WithName("codeGenerationRetryRunPublication")
        .Produces<CodeGenerationRunPublicationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(CodeGenerationRunPermissions.Apply));

        group.MapGet("/{runId:guid}/artifacts.zip", async (
            Guid runId,
            CodeGenerationArtifactDownloadService downloads,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await downloads.DownloadAsync(runId, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.File(
                new MemoryStream(result.Value!.Content),
                "application/octet-stream",
                result.Value.FileName);
        })
        .WithName("codeGenerationDownloadRunArtifacts")
        .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationRunPermissions.Download));
    }

    private static bool TryResolveUserId(
        HttpContext httpContext,
        out Guid userId)
    {
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
