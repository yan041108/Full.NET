using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.ManageHostTemplates;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 将已审查的模板预览绑定到服务器本地工作区，并记录不含源码和路径的不可变摘要。
/// </summary>
internal sealed class CodeGenerationApplyService(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor,
    CodeGenerationTemplateQueryService templateQueries,
    CodeGenerationSchemaNormalizer schemaNormalizer,
    IOptions<CodeGenerationApplyOptions> options,
    CodeGenerationApplyGate applyGate,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<Result<CodeGenerationRunApplyResponse>> ApplyAsync(
        Guid actorUserId,
        CodeGenerationRunApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.Enabled)
        {
            return Failure(
                CodeGenerationRunErrorCodes.ApplyDisabled,
                "Code generation apply is disabled.",
                ErrorType.Conflict);
        }

        var preview = await queryExecutor
            .QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindById,
                new { Id = request.PreviewRunId },
                cancellationToken)
            .ConfigureAwait(false);
        if (preview is null
            || preview.OperationKind != CodeGenerationRunOperationKinds.Preview
            || preview.Status != CodeGenerationRunStatuses.Succeeded
            || !preview.TemplateId.HasValue
            || preview.TemplateVersion is not > 0
            || preview.SchemaSha256 is null
            || preview.ManifestSha256 is null)
        {
            return Failure(
                CodeGenerationRunErrorCodes.InvalidApplyPreview,
                "The reviewed code generation preview cannot be applied.",
                ErrorType.Validation);
        }

        var template = await templateQueries.GetByIdAsync(
                preview.TemplateId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (!template.IsSuccess)
        {
            return Failure(
                CodeGenerationRunErrorCodes.StaleApplyPreview,
                "The reviewed code generation preview is stale.",
                ErrorType.Conflict);
        }

        if (template.Value!.Version != preview.TemplateVersion)
        {
            return Failure(
                CodeGenerationRunErrorCodes.StaleApplyPreview,
                "The reviewed code generation preview is stale.",
                ErrorType.Conflict);
        }

        var normalized = schemaNormalizer.Normalize(template.Value.Schema);
        if (!normalized.IsSuccess)
        {
            return Result<CodeGenerationRunApplyResponse>.Failure(
                normalized.Error!);
        }

        var artifacts = CrudArtifactGenerator.Generate(
            normalized.Value!.Schema);
        var manifestSha256 = CodeGenerationRunSummary
            .ComputeManifestSha256(artifacts);
        if (!StringComparer.Ordinal.Equals(
                normalized.Value.SchemaSha256,
                preview.SchemaSha256)
            || !StringComparer.Ordinal.Equals(
                manifestSha256,
                preview.ManifestSha256)
            || artifacts.Count != preview.ArtifactCount)
        {
            return Failure(
                CodeGenerationRunErrorCodes.StaleApplyPreview,
                "The reviewed code generation preview is stale.",
                ErrorType.Conflict);
        }

        if (!await applyGate.TryEnterAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return Failure(
                CodeGenerationRunErrorCodes.ApplyBusy,
                "Another code generation apply is in progress.",
                ErrorType.Conflict);
        }

        try
        {
            var runId = idGenerator.NewId();
            var startedAtUtc = clock.UtcNow;
            var insertedRows = await commandExecutor.ExecuteAsync(
                    CodeGenerationRunSql.Insert,
                    new
                    {
                        Id = runId,
                        preview.TemplateId,
                        preview.TemplateVersion,
                        SourceApplyRunId = (Guid?)null,
                        OperationKind = CodeGenerationRunOperationKinds.Apply,
                        Status = CodeGenerationRunStatuses.Running,
                        normalized.Value.Schema.ModuleKey,
                        normalized.Value.Schema.EntityKey,
                        normalized.Value.SchemaSha256,
                        ArtifactCount = artifacts.Count,
                        ManifestSha256 = manifestSha256,
                        ErrorCode = (string?)null,
                        RequestedByUserId = actorUserId,
                        StartedAtUtc = startedAtUtc,
                        FinishedAtUtc = startedAtUtc,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureAffectedOne(insertedRows, "insert");

            GenerationWritePlan plan;
            try
            {
                plan = await CrudGenerationWorkspace.PlanAsync(
                        options.Value.WorkspaceRoot,
                        normalized.Value.Schema,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await FailAsync(
                        runId,
                        CodeGenerationRunErrorCodes.ApplyFailed,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (exception is
                DirectoryNotFoundException
                or IOException
                or UnauthorizedAccessException
                or GenerationWorkspaceConflictException)
            {
                await FailAsync(
                        runId,
                        CodeGenerationRunErrorCodes.ApplyFailed,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return Failure(
                    CodeGenerationRunErrorCodes.ApplyFailed,
                    "The code generation apply failed.",
                    ErrorType.Unexpected);
            }
            if (!plan.CanApply)
            {
                await FailAsync(
                        runId,
                        CodeGenerationRunErrorCodes.ApplyConflict,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return Failure(
                    CodeGenerationRunErrorCodes.ApplyConflict,
                    "The code generation workspace contains conflicts.",
                    ErrorType.Conflict);
            }

            try
            {
                await GenerationRollbackCheckpointStore.CreateAsync(
                        options.Value.WorkspaceRoot,
                        runId,
                        plan,
                        cancellationToken)
                    .ConfigureAwait(false);
                await GenerationWorkspaceStore.ApplyAsync(
                        options.Value.WorkspaceRoot,
                        plan,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await FailAsync(
                        runId,
                        CodeGenerationRunErrorCodes.ApplyFailed,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (exception is
                DirectoryNotFoundException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or GenerationWorkspaceConflictException)
            {
                await FailAsync(
                        runId,
                        CodeGenerationRunErrorCodes.ApplyFailed,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return Failure(
                    CodeGenerationRunErrorCodes.ApplyFailed,
                    "The code generation apply failed.",
                    ErrorType.Unexpected);
            }

            var completedRows = await commandExecutor.ExecuteAsync(
                    CodeGenerationRunSql.CompleteApply,
                    new
                    {
                        Id = runId,
                        FinishedAtUtc = clock.UtcNow,
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            EnsureAffectedOne(completedRows, "completion");

            var changedCount = plan.Actions.Count(action =>
                action.Kind != GenerationWriteActionKind.Unchanged);
            return Result<CodeGenerationRunApplyResponse>.Success(
                new CodeGenerationRunApplyResponse(
                    runId,
                    request.PreviewRunId,
                    artifacts.Count,
                    changedCount,
                    manifestSha256));
        }
        finally
        {
            applyGate.Release();
        }
    }

    private async Task FailAsync(
        Guid runId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var affectedRows = await commandExecutor.ExecuteAsync(
                CodeGenerationRunSql.FailApply,
                new
                {
                    Id = runId,
                    ErrorCode = errorCode,
                    FinishedAtUtc = clock.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        EnsureAffectedOne(affectedRows, "failure completion");
    }

    private static void EnsureAffectedOne(int affectedRows, string operation)
    {
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Code generation run {operation} affected {affectedRows} rows instead of one.");
        }
    }

    private static Result<CodeGenerationRunApplyResponse> Failure(
        string code,
        string message,
        ErrorType type) =>
        Result<CodeGenerationRunApplyResponse>.Failure(
            new Error(code, message, type));
}
