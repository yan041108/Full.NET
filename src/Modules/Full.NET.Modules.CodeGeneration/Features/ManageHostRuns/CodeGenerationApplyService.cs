using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Git;
using Full.NET.Modules.CodeGeneration.Features.ManageHostTemplates;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Persistence;
using Full.NET.Modules.CodeGeneration.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>把内存预览转换为可审查的完整工作区计划，确认后只提交冻结内容并按耐久幂等键返回实际结果。</summary>
internal sealed class CodeGenerationApplyService(ICommandExecutor commandExecutor, IQueryExecutor queryExecutor,
    CodeGenerationSchemaNormalizer schemaNormalizer,
    IOptions<CodeGenerationApplyOptions> options, CodeGenerationApplyGate applyGate,
    CodeGenerationGitWorkspaceService gitWorkspace, IClock clock, IIdGenerator idGenerator, CodeGenerationArtifactStore archives)
{
    public async Task<Result<CodeGenerationRunApplyPlanResponse>> PrepareAsync(Guid actorUserId,
        CodeGenerationRunApplyPlanRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!options.Value.Enabled) return Fail<CodeGenerationRunApplyPlanResponse>(CodeGenerationRunErrorCodes.ApplyDisabled, "Code generation apply is disabled.");
        ModuleIntegrationTarget? target;
        try { target = request.IntegrationTarget is null ? null : MapIntegrationTarget(request.IntegrationTarget); }
        catch (ArgumentException) { return Fail<CodeGenerationRunApplyPlanResponse>(CodeGenerationRunErrorCodes.InvalidApplyPreview, "The integration target is invalid.", ErrorType.Validation); }
        if (!Directory.Exists(options.Value.WorkspaceRoot)) return Fail<CodeGenerationRunApplyPlanResponse>(CodeGenerationRunErrorCodes.ApplyFailed, "The configured workspace is unavailable.");
        if (!await applyGate.TryEnterAsync(token).ConfigureAwait(false)) return Busy<CodeGenerationRunApplyPlanResponse>();
        try
        {
            await RecoverAsync(token).ConfigureAwait(false);
            var preview = await queryExecutor.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(CodeGenerationRunSql.FindById,
                CodeGenerationSqlParameters.Create(("Id", request.PreviewRunId)), token).ConfigureAwait(false);
            if (preview is not { OperationKind: "preview", Status: "succeeded",
                SchemaSha256: not null, ManifestSha256: not null })
                return Fail<CodeGenerationRunApplyPlanResponse>(CodeGenerationRunErrorCodes.InvalidApplyPreview, "The preview cannot be planned.", ErrorType.Validation);
            var archive = await archives.ReadAsync(preview.Id, token).ConfigureAwait(false);
            if (archive.Record.ManifestSha256 != preview.ManifestSha256 || archive.Record.SchemaSha256 != preview.SchemaSha256
                || archive.Record.GeneratorVersion != CrudArtifactGenerator.GeneratorVersion)
                return Fail<CodeGenerationRunApplyPlanResponse>(CodeGenerationRunErrorCodes.StaleApplyPreview, "The archived preview is stale.");
            var input = JsonSerializer.Deserialize(archive.Record.SchemaJson, CodeGenerationJsonSerializerContext.Default.CodeGenerationPreviewRequest);
            if (input is null) return Fail<CodeGenerationRunApplyPlanResponse>(CodeGenerationRunErrorCodes.InvalidApplyPreview, "The archived schema is invalid.");
            var normalized = schemaNormalizer.Normalize(input);
            if (!normalized.IsSuccess) return Result<CodeGenerationRunApplyPlanResponse>.Failure(normalized.Error!);
            var schema = normalized.Value!.Schema;
            var artifacts = CrudArtifactGenerator.Generate(schema);
            if (normalized.Value.SchemaSha256 != preview.SchemaSha256 || CodeGenerationRunSummary.ComputeManifestSha256(artifacts) != preview.ManifestSha256
                || artifacts.Count != preview.ArtifactCount) return Fail<CodeGenerationRunApplyPlanResponse>(CodeGenerationRunErrorCodes.StaleApplyPreview, "The preview is stale.");
            var plan = await CrudGenerationWorkspace.PlanAsync(options.Value.WorkspaceRoot, schema, token).ConfigureAwait(false);
            if (target is not null)
            {
                plan = await ModuleIntegrationFullPlanner.PlanAsync(options.Value.WorkspaceRoot, schema, target, plan, token).ConfigureAwait(false);
                if (plan.CanApply)
                {
                    var compilation = await ModuleIntegrationFullCompilation.ValidateAsync(options.Value.WorkspaceRoot, schema, target, plan, token).ConfigureAwait(false);
                    if (!compilation.Succeeded) return Fail<CodeGenerationRunApplyPlanResponse>(CodeGenerationRunErrorCodes.ApplyFailed, "The complete candidate did not compile.", ErrorType.Validation);
                }
            }
            if (!plan.CanApply) return Fail<CodeGenerationRunApplyPlanResponse>(CodeGenerationRunErrorCodes.ApplyConflict, "The complete plan contains conflicts.");
            var syncError = await gitWorkspace.SynchronizeAsync(token).ConfigureAwait(false);
            if (syncError is not null) return Result<CodeGenerationRunApplyPlanResponse>.Failure(syncError);
            var id = idGenerator.NewId();
            var now = clock.UtcNow;
            var expires = now.AddMinutes(15);
            var run = new CodeGenerationRunRecord { Id = id, TemplateId = preview.TemplateId, TemplateVersion = preview.TemplateVersion,
                OperationKind = "apply", Status = "running", ModuleKey = schema.ModuleKey, EntityKey = schema.EntityKey,
                SchemaSha256 = normalized.Value.SchemaSha256, ArtifactCount = plan.Actions.Count,
                ManifestSha256 = CodeGenerationRunSummary.ComputeManifestSha256(plan.NextManifest!), RequestedByUserId = actorUserId,
                StartedAtUtc = now, FinishedAtUtc = now };
            var publication = gitWorkspace.CapturePublicationContext(id, $"codegen(apply): {schema.ModuleKey}/{schema.EntityKey} run {id:N}", plan);
            var context = new CodeGenerationWorkspaceRunContext(1, run, request.PreviewRunId, expires, CrudArtifactGenerator.GeneratorVersion,
                normalized.Value.CanonicalJson, request.IntegrationTarget, publication);
            await PruneExpiredPlansAsync(token).ConfigureAwait(false);
            var frozen = await GenerationWorkspaceJournal.FreezeAsync(applyGate.Lease, id, id, "apply", "host",
                CodeGenerationWorkspaceRecovery.SerializeContext(context), plan, cancellationToken: token).ConfigureAwait(false);
            if (await GenerationWorkspaceMetadata.ReadAsync(options.Value.WorkspaceRoot, PlanPath(id), token).ConfigureAwait(false) is not null)
                return Fail<CodeGenerationRunApplyPlanResponse>(CodeGenerationRunErrorCodes.ApplyConflict, "The plan identity has already been reserved.");
            await GenerationWorkspaceMetadata.WriteAsync(options.Value.WorkspaceRoot, PlanPath(id), GenerationWorkspaceJournal.SerializePlan(frozen), token).ConfigureAwait(false);
            return Result<CodeGenerationRunApplyPlanResponse>.Success(new(id, request.PreviewRunId, applyGate.WorkspaceId,
                frozen.PreviousHead.Revision, context.GeneratorVersion, frozen.PlanSha256, expires,
                plan.Actions.Select(action => new CodeGenerationApplyPlanFileResponse(action.RelativePath,
                    action.Kind.ToString().ToLowerInvariant(), action.ExistingSha256, action.DesiredSha256))
                    .Append(new(GenerationWorkspaceStore.ManifestRelativePath, "manifest", frozen.Files[^1].Before is null ? null : GenerationContentHash.Compute(frozen.Files[^1].Before!),
                        GenerationContentHash.Compute(frozen.Files[^1].After!))).ToArray()));
        }
        catch (CodeGenerationArchiveException exception)
        { return Fail<CodeGenerationRunApplyPlanResponse>(exception.Code, "The saved preview archive is unavailable."); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { return Fail<CodeGenerationRunApplyPlanResponse>(CodeGenerationRunErrorCodes.ApplyConflict, "The workspace or recovery evidence requires review."); }
        finally { await applyGate.ReleaseAsync().ConfigureAwait(false); }
    }

    public async Task<Result<CodeGenerationRunApplyResponse>> ApplyAsync(Guid actorUserId, CodeGenerationRunApplyRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!options.Value.Enabled) return Fail<CodeGenerationRunApplyResponse>(CodeGenerationRunErrorCodes.ApplyDisabled, "Code generation apply is disabled.");
        if (request.PlanId == Guid.Empty || request.IdempotencyKey == Guid.Empty || !GenerationContentHash.IsValid(request.PlanSha256))
            return Fail<CodeGenerationRunApplyResponse>(CodeGenerationRunErrorCodes.InvalidApplyPreview, "A reviewed plan and idempotency key are required.", ErrorType.Validation);
        if (!Directory.Exists(options.Value.WorkspaceRoot)) return Fail<CodeGenerationRunApplyResponse>(CodeGenerationRunErrorCodes.ApplyFailed, "The configured workspace is unavailable.");
        if (!await applyGate.TryEnterAsync(token).ConfigureAwait(false)) return Busy<CodeGenerationRunApplyResponse>();
        try
        {
            await RecoverAsync(token).ConfigureAwait(false);
            var existing = await GenerationWorkspaceJournal.FindByIdempotencyKeyAsync(applyGate.Lease, request.IdempotencyKey, token).ConfigureAwait(false);
            if (existing is not null)
            {
                if (existing.Id != request.PlanId || existing.PlanSha256 != request.PlanSha256)
                    return Fail<CodeGenerationRunApplyResponse>(CodeGenerationRunErrorCodes.ApplyConflict, "The idempotency key belongs to a different plan.");
                if (existing.Phase == "prepared")
                {
                    if (CodeGenerationWorkspaceRecovery.ReadContext(existing).Run.RequestedByUserId != actorUserId)
                        return Fail<CodeGenerationRunApplyResponse>(CodeGenerationRunErrorCodes.ApplyConflict, "The operation belongs to another reviewer.");
                    // 收据已写入但活动指针尚未发布时进程可能中断；恢复同一预留，不能永远卡住该幂等键。
                    await GenerationWorkspaceJournal.ActivateAsync(applyGate.Lease, existing, token).ConfigureAwait(false);
                    await RecoverAsync(token).ConfigureAwait(false);
                    existing = (await GenerationWorkspaceJournal.ReadOperationAsync(applyGate.Lease, existing.Id, token).ConfigureAwait(false))!;
                }
                return await RespondAsync(existing, actorUserId, token).ConfigureAwait(false);
            }
            var json = await GenerationWorkspaceMetadata.ReadAsync(options.Value.WorkspaceRoot, PlanPath(request.PlanId), token).ConfigureAwait(false);
            if (json is null) return Fail<CodeGenerationRunApplyResponse>(CodeGenerationRunErrorCodes.StaleApplyPreview, "The reviewed plan is unavailable.");
            var frozen = GenerationWorkspaceJournal.ParsePlan(applyGate.Lease, json);
            var context = CodeGenerationWorkspaceRecovery.ReadContext(frozen);
            if (frozen.Id != request.PlanId || frozen.PlanSha256 != request.PlanSha256 || context.Run.RequestedByUserId != actorUserId
                || context.ExpiresAtUtc <= clock.UtcNow || context.GeneratorVersion != CrudArtifactGenerator.GeneratorVersion)
                return Fail<CodeGenerationRunApplyResponse>(CodeGenerationRunErrorCodes.StaleApplyPreview, "The reviewed plan is stale or belongs to another reviewer.");
            // 复核 Git 基线；fetch 不改变文件，任何基线差异均使旧计划失效。
            var syncError = await gitWorkspace.SynchronizeAsync(token).ConfigureAwait(false);
            if (syncError is not null) return Result<CodeGenerationRunApplyResponse>.Failure(syncError);
            if (!gitWorkspace.MatchesPublicationBaseline(context.PublicationContext))
                return Fail<CodeGenerationRunApplyResponse>(CodeGenerationRunErrorCodes.StaleApplyPreview, "The reviewed Git baseline changed.");
            await GenerationWorkspaceJournal.ActivateAsync(applyGate.Lease, frozen with { IdempotencyKey = request.IdempotencyKey }, token).ConfigureAwait(false);
            await RecoverAsync(token).ConfigureAwait(false);
            return await RespondAsync((await GenerationWorkspaceJournal.ReadOperationAsync(applyGate.Lease, frozen.Id, token).ConfigureAwait(false))!, actorUserId, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            var recoveryRequired = await HasOperationEvidenceAsync(request.IdempotencyKey).ConfigureAwait(false);
            return Fail<CodeGenerationRunApplyResponse>(recoveryRequired ? CodeGenerationRunErrorCodes.RecoveryRequired : CodeGenerationRunErrorCodes.ApplyConflict,
                recoveryRequired ? "The workspace requires recovery; retry the same plan and idempotency key." : "The reviewed workspace plan is stale or contains conflicts.");
        }
        finally { await applyGate.ReleaseAsync().ConfigureAwait(false); }
    }

    private async Task<Result<CodeGenerationRunApplyResponse>> RespondAsync(GenerationWorkspaceOperation operation, Guid actor, CancellationToken token)
    {
        var context = CodeGenerationWorkspaceRecovery.ReadContext(operation);
        if (operation.Kind != "apply" || context.Run.RequestedByUserId != actor || operation.Phase != "completed")
            return Fail<CodeGenerationRunApplyResponse>(CodeGenerationRunErrorCodes.ApplyConflict, "The operation has not completed for this reviewer.");
        var publication = await gitWorkspace.GetSavedPublicationAsync(operation.Id, token).ConfigureAwait(false);
        return Result<CodeGenerationRunApplyResponse>.Success(new(operation.Id, context.PreviewRunId!.Value, context.Run.ArtifactCount,
            operation.Files.Length - 1, context.Run.ManifestSha256!, publication.Status, publication.CommitSha));
    }

    private async Task PruneExpiredPlansAsync(CancellationToken token)
    {
        var directory = Path.Combine(options.Value.WorkspaceRoot, ".fullnet", "codegeneration-plans");
        if (!Directory.Exists(directory)) return;
        // 只删除过期且尚未启动的计划。读取仍经元数据路径防护；未知文件保留给总量准入拒绝。
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id)) continue;
            var json = await GenerationWorkspaceMetadata.ReadAsync(options.Value.WorkspaceRoot, PlanPath(id), token).ConfigureAwait(false);
            if (json is null) continue;
            var operation = GenerationWorkspaceJournal.ParsePlan(applyGate.Lease, json);
            if (CodeGenerationWorkspaceRecovery.ReadContext(operation).ExpiresAtUtc > clock.UtcNow) continue;
            var started = await GenerationWorkspaceJournal.ReadOperationAsync(applyGate.Lease, id, token).ConfigureAwait(false);
            if (started is not null && started.Phase != "completed") continue;
            GenerationWorkspaceMetadata.Delete(options.Value.WorkspaceRoot, PlanPath(id));
        }
    }

    private Task RecoverAsync(CancellationToken token) => CodeGenerationWorkspaceRecovery.RecoverAsync(applyGate.Lease, options.Value.WorkspaceRoot,
        commandExecutor, queryExecutor, gitWorkspace, clock, archives, token);
    private async Task<bool> HasOperationEvidenceAsync(Guid key)
    {
        try
        {
            return await GenerationWorkspaceJournal.ReadPendingAsync(applyGate.Lease).ConfigureAwait(false) is not null
                || await GenerationWorkspaceJournal.FindByIdempotencyKeyAsync(applyGate.Lease, key).ConfigureAwait(false) is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { return true; }
    }
    private static string PlanPath(Guid id) => $".fullnet/codegeneration-plans/{id:N}.json";
    private static Result<T> Busy<T>() => Fail<T>(CodeGenerationRunErrorCodes.ApplyBusy, "Another code generation operation is in progress.");
    private static Result<T> Fail<T>(string code, string message, ErrorType type = ErrorType.Conflict) => Result<T>.Failure(new Error(code, message, type));

    private static ModuleIntegrationTarget MapIntegrationTarget(CodeGenerationIntegrationTargetRequest request) => ModuleIntegrationTarget.Create(
        request.ModuleName, request.ModuleProjectPath, request.ModuleEntryPointPath, request.CompositionProjectPath, request.CompositionCatalogPath,
        request.VueRouterPath, request.LayuiRouterPath, request.ClientRoute is null ? null : ModuleClientRouteTarget.Create(request.ClientRoute.RoutePath,
            request.ClientRoute.VueRouteName, request.ClientRoute.VueComponentPath, request.ClientRoute.LayuiControllerPath, request.ClientRoute.LayuiControllerExport), request.AuthorizationContributorPath);
}
