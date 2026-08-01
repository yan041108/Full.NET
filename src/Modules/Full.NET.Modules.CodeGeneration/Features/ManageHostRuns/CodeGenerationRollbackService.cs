using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Git;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 以 DB 成功 Apply 为资格权威，复用共享 Gate 与内部 RollbackWorkspace 完成产品回滚。
/// </summary>
internal sealed class CodeGenerationRollbackService(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor,
    IOptions<CodeGenerationApplyOptions> options,
    IOptions<CodeGenerationCheckpointRetentionOptions> retentionOptions,
    CodeGenerationApplyGate applyGate,
    CodeGenerationGitWorkspaceService gitWorkspace,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<CodeGenerationRollbackService> logger)
{
    public async Task<Result<CodeGenerationRunRollbackResponse>> RollbackAsync(
        Guid actorUserId,
        CodeGenerationRunRollbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.Enabled)
        {
            return Failure(
                CodeGenerationRunErrorCodes.RollbackDisabled,
                "Code generation rollback is disabled.",
                ErrorType.Conflict);
        }

        var apply = await queryExecutor
            .QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindById,
                new { Id = request.ApplyRunId },
                cancellationToken)
            .ConfigureAwait(false);
        if (apply is null
            || apply.OperationKind != CodeGenerationRunOperationKinds.Apply
            || apply.Status != CodeGenerationRunStatuses.Succeeded
            || apply.ModuleKey is null
            || apply.EntityKey is null
            || apply.SchemaSha256 is null)
        {
            return Failure(
                CodeGenerationRunErrorCodes.InvalidRollbackApply,
                "The selected code generation apply cannot be rolled back.",
                ErrorType.Validation);
        }

        var runningRollback = await queryExecutor
            .QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindRunningRollbackBySourceApplyRunId,
                new { SourceApplyRunId = request.ApplyRunId },
                cancellationToken)
            .ConfigureAwait(false);
        if (runningRollback is not null)
        {
            return Failure(
                CodeGenerationRunErrorCodes.RollbackBusy,
                "Another code generation apply or rollback is in progress.",
                ErrorType.Conflict);
        }

        var existingRollback = await queryExecutor
            .QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindSucceededRollbackBySourceApplyRunId,
                new { SourceApplyRunId = request.ApplyRunId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existingRollback is not null)
        {
            return await TryReplaySucceededRollbackAsync(
                    existingRollback,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!await applyGate.TryEnterAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return Failure(
                CodeGenerationRunErrorCodes.RollbackBusy,
                "Another code generation apply or rollback is in progress.",
                ErrorType.Conflict);
        }

        try
        {
            var syncError = await gitWorkspace
                .SynchronizeAsync(cancellationToken)
                .ConfigureAwait(false);
            if (syncError is not null)
            {
                return Failure(
                    syncError.Code,
                    syncError.Message,
                    syncError.Type);
            }

            // 目标摘要在写盘前即可由检查点 PreviousManifest 确定；插入后再 Restore。
            GenerationRollbackCheckpoint checkpoint;
            try
            {
                checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(
                        options.Value.WorkspaceRoot,
                        request.ApplyRunId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is
                DirectoryNotFoundException
                or FileNotFoundException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException
                or JsonException)
            {
                return Failure(
                    CodeGenerationRunErrorCodes.RollbackCheckpointMissing,
                    "The rollback checkpoint is unavailable.",
                    ErrorType.Conflict);
            }

            var targetManifest = checkpoint.PreviousManifest
                ?? GenerationManifest.Create([]);
            var manifestSha256 = CodeGenerationRunSummary.ComputeManifestSha256(
                targetManifest);
            var runId = idGenerator.NewId();
            var startedAtUtc = clock.UtcNow;
            var insertedRows = await commandExecutor.ExecuteAsync(
                    CodeGenerationRunSql.Insert,
                    new
                    {
                        Id = runId,
                        TemplateId = (Guid?)null,
                        TemplateVersion = (long?)null,
                        SourceApplyRunId = request.ApplyRunId,
                        OperationKind = CodeGenerationRunOperationKinds.Rollback,
                        Status = CodeGenerationRunStatuses.Running,
                        apply.ModuleKey,
                        apply.EntityKey,
                        apply.SchemaSha256,
                        ArtifactCount = targetManifest.Artifacts.Count,
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
                plan = await GenerationRollbackWorkspace.RestoreAsync(
                        options.Value.WorkspaceRoot,
                        checkpoint,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await FailAsync(
                        runId,
                        CodeGenerationRunErrorCodes.RollbackFailed,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
            catch (GenerationWorkspaceConflictException)
            {
                await FailAsync(
                        runId,
                        CodeGenerationRunErrorCodes.RollbackConflict,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return Failure(
                    CodeGenerationRunErrorCodes.RollbackConflict,
                    "The code generation workspace contains conflicts.",
                    ErrorType.Conflict);
            }
            catch (Exception exception) when (exception is
                DirectoryNotFoundException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException)
            {
                await FailAsync(
                        runId,
                        CodeGenerationRunErrorCodes.RollbackFailed,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return Failure(
                    CodeGenerationRunErrorCodes.RollbackFailed,
                    "The code generation rollback failed.",
                    ErrorType.Unexpected);
            }

            if (!plan.CanApply)
            {
                await FailAsync(
                        runId,
                        CodeGenerationRunErrorCodes.RollbackConflict,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return Failure(
                    CodeGenerationRunErrorCodes.RollbackConflict,
                    "The code generation workspace contains conflicts.",
                    ErrorType.Conflict);
            }

            var completedRows = await commandExecutor.ExecuteAsync(
                    CodeGenerationRunSql.CompleteRollback,
                    new
                    {
                        Id = runId,
                        FinishedAtUtc = clock.UtcNow,
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            EnsureAffectedOne(completedRows, "completion");

            await gitWorkspace.PublishAsync(
                    $"codegen(rollback): {apply.ModuleKey}/{apply.EntityKey} "
                    + $"apply {request.ApplyRunId:N}",
                    CancellationToken.None)
                .ConfigureAwait(false);

            await TryDeleteCheckpointAfterSucceededRollbackAsync(
                    request.ApplyRunId,
                    cancellationToken)
                .ConfigureAwait(false);

            var changedCount = plan.Actions.Count(action =>
                action.Kind != GenerationWriteActionKind.Unchanged);
            return Result<CodeGenerationRunRollbackResponse>.Success(
                ToResponse(
                    runId,
                    request.ApplyRunId,
                    targetManifest.Artifacts.Count,
                    changedCount,
                    manifestSha256));
        }
        finally
        {
            applyGate.Release();
        }
    }

    private async Task<Result<CodeGenerationRunRollbackResponse>> TryReplaySucceededRollbackAsync(
        CodeGenerationRunRecord existingRollback,
        CancellationToken cancellationToken)
    {
        if (existingRollback.SourceApplyRunId is null
            || existingRollback.ManifestSha256 is null)
        {
            return Failure(
                CodeGenerationRunErrorCodes.RollbackConflict,
                "The code generation workspace contains conflicts.",
                ErrorType.Conflict);
        }

        GenerationManifest currentManifest;
        try
        {
            currentManifest = await GenerationWorkspaceStore.ReadManifestOrEmptyAsync(
                    options.Value.WorkspaceRoot,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException
            or UnauthorizedAccessException
            or ArgumentException
            or JsonException)
        {
            return Failure(
                CodeGenerationRunErrorCodes.RollbackConflict,
                "The code generation workspace contains conflicts.",
                ErrorType.Conflict);
        }

        var currentSha256 = CodeGenerationRunSummary.ComputeManifestSha256(
            currentManifest);
        if (!string.Equals(
                currentSha256,
                existingRollback.ManifestSha256,
                StringComparison.Ordinal))
        {
            return Failure(
                CodeGenerationRunErrorCodes.RollbackConflict,
                "The code generation workspace contains conflicts.",
                ErrorType.Conflict);
        }

        return Result<CodeGenerationRunRollbackResponse>.Success(
            ToResponse(
                existingRollback.Id,
                existingRollback.SourceApplyRunId.Value,
                existingRollback.ArtifactCount,
                changedArtifactCount: 0,
                existingRollback.ManifestSha256));
    }

    private async Task TryDeleteCheckpointAfterSucceededRollbackAsync(
        Guid applyRunId,
        CancellationToken cancellationToken)
    {
        if (!retentionOptions.Value.DeleteAfterSucceededRollback)
        {
            return;
        }

        try
        {
            await GenerationRollbackCheckpointStore.TryDeleteAsync(
                    options.Value.WorkspaceRoot,
                    applyRunId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            logger.LogWarning(
                exception,
                "Failed to delete rollback checkpoint after succeeded rollback.");
        }
    }

    private async Task FailAsync(
        Guid runId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var affectedRows = await commandExecutor.ExecuteAsync(
                CodeGenerationRunSql.FailRollback,
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

    private static CodeGenerationRunRollbackResponse ToResponse(
        Guid runId,
        Guid applyRunId,
        int artifactCount,
        int changedArtifactCount,
        string manifestSha256) =>
        new(
            runId,
            applyRunId,
            artifactCount,
            changedArtifactCount,
            manifestSha256);

    private static void EnsureAffectedOne(int affectedRows, string operation)
    {
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Code generation rollback {operation} affected {affectedRows} rows instead of one.");
        }
    }

    private static Result<CodeGenerationRunRollbackResponse> Failure(
        string code,
        string message,
        ErrorType type) =>
        Result<CodeGenerationRunRollbackResponse>.Failure(
            new Error(code, message, type));
}
