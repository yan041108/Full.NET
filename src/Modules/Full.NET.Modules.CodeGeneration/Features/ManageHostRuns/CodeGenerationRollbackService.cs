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
    /// <summary>
    /// 回滚单次已成功 Apply：以 DB 成功 Apply 为资格权威，拒绝 running 回滚并对已 succeeded 的回滚按幂等重放；
    /// 通过 ApplyGate 串行化后，从检查点恢复工作区，失败路径将回滚运行标记为 failed；成功后按保留策略可选删除检查点并发布 Git 提交。
    /// </summary>
    /// <remarks>
    /// 重放路径会重新读取工作区当前 Manifest 并与历史回滚摘要比对，不一致返回 RollbackConflict，避免把已被外部改动的工作区误判为已回滚。
    /// </remarks>
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
                CodeGenerationSqlParameters.Create(("Id", request.ApplyRunId)),
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
                CodeGenerationSqlParameters.Create(("SourceApplyRunId", request.ApplyRunId)),
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
                CodeGenerationSqlParameters.Create(("SourceApplyRunId", request.ApplyRunId)),
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

            return await ExecuteNewRollbackAsync(
                    actorUserId,
                    apply,
                    request.ApplyRunId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            applyGate.Release();
        }
    }

    /// <summary>
    /// 按 LIFO 顺序回滚多个已成功 Apply：校验链长度（2..MaxRollbackChainLength）、去重、单一模块/实体且顺序与待回滚列表完全一致，任一不匹配返回 InvalidRollbackChain；
    /// 串行执行每步回滚，已 succeeded 的步骤幂等重放，中途失败立即停止并返回已完成步骤。
    /// </summary>
    /// <remarks>
    /// 顺序约束来自 ListPendingRollbackApplies 的 LIFO 投影，确保回滚不会跳过较新的 Apply 而破坏工作区一致性。
    /// </remarks>
    public async Task<Result<CodeGenerationRunRollbackChainResponse>> RollbackChainAsync(
        Guid actorUserId,
        CodeGenerationRunRollbackChainRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.Enabled)
        {
            return FailureChain(
                CodeGenerationRunErrorCodes.RollbackDisabled,
                "Code generation rollback is disabled.",
                ErrorType.Conflict);
        }

        var validation = await ValidateRollbackChainAsync(
                request.ApplyRunIds,
                cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            return Result<CodeGenerationRunRollbackChainResponse>.Failure(
                validation.Error!);
        }

        foreach (var applyRunId in request.ApplyRunIds)
        {
            var runningRollback = await queryExecutor
                .QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                    CodeGenerationRunSql.FindRunningRollbackBySourceApplyRunId,
                    CodeGenerationSqlParameters.Create(("SourceApplyRunId", applyRunId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (runningRollback is not null)
            {
                return FailureChain(
                    CodeGenerationRunErrorCodes.RollbackBusy,
                    "Another code generation apply or rollback is in progress.",
                    ErrorType.Conflict);
            }
        }

        if (!await applyGate.TryEnterAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return FailureChain(
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
                return FailureChain(
                    syncError.Code,
                    syncError.Message,
                    syncError.Type);
            }

            var rollbacks = new List<CodeGenerationRunRollbackResponse>(
                request.ApplyRunIds.Count);
            foreach (var apply in validation.Value!)
            {
                var existingRollback = await queryExecutor
                    .QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                        CodeGenerationRunSql.FindSucceededRollbackBySourceApplyRunId,
                        CodeGenerationSqlParameters.Create(("SourceApplyRunId", apply.Id)),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (existingRollback is not null)
                {
                    var replay = await TryReplaySucceededRollbackAsync(
                            existingRollback,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!replay.IsSuccess)
                    {
                        return Result<CodeGenerationRunRollbackChainResponse>.Failure(
                            replay.Error!);
                    }

                    rollbacks.Add(replay.Value!);
                    continue;
                }

                var step = await ExecuteNewRollbackAsync(
                        actorUserId,
                        apply,
                        apply.Id,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!step.IsSuccess)
                {
                    return Result<CodeGenerationRunRollbackChainResponse>.Failure(
                        step.Error!);
                }

                rollbacks.Add(step.Value!);
            }

            return Result<CodeGenerationRunRollbackChainResponse>.Success(
                new CodeGenerationRunRollbackChainResponse(rollbacks));
        }
        finally
        {
            applyGate.Release();
        }
    }

    private async Task<Result<IReadOnlyList<CodeGenerationRunRecord>>>
        ValidateRollbackChainAsync(
            IReadOnlyList<Guid> applyRunIds,
            CancellationToken cancellationToken)
    {
        if (applyRunIds.Count < 2)
        {
            return InvalidChain(
                "A rollback chain must include at least two apply runs.");
        }

        if (applyRunIds.Count > options.Value.MaxRollbackChainLength)
        {
            return InvalidChain(
                "The rollback chain exceeds the configured maximum length.");
        }

        if (applyRunIds.Distinct().Count() != applyRunIds.Count)
        {
            return InvalidChain(
                "The rollback chain contains duplicate apply runs.");
        }

        var applies = new List<CodeGenerationRunRecord>(applyRunIds.Count);
        foreach (var applyRunId in applyRunIds)
        {
            var apply = await queryExecutor
                .QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                    CodeGenerationRunSql.FindById,
                    CodeGenerationSqlParameters.Create(("Id", applyRunId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (apply is null
                || apply.OperationKind != CodeGenerationRunOperationKinds.Apply
                || apply.Status != CodeGenerationRunStatuses.Succeeded
                || apply.ModuleKey is null
                || apply.EntityKey is null
                || apply.SchemaSha256 is null)
            {
                return InvalidChain(
                    "The rollback chain contains an apply that cannot be rolled back.");
            }

            applies.Add(apply);
        }

        var moduleKey = applies[0].ModuleKey!;
        var entityKey = applies[0].EntityKey!;
        if (!applies.All(item =>
                item.ModuleKey == moduleKey
                && item.EntityKey == entityKey))
        {
            return InvalidChain(
                "The rollback chain must target a single module and entity.");
        }

        var pendingApplyRunIds = (await queryExecutor
                .QueryAsync<Guid>(
                    CodeGenerationRunSql.ListPendingRollbackApplies,
                    CodeGenerationSqlParameters.Create(
                        ("ModuleKey", moduleKey),
                        ("EntityKey", entityKey)),
                    cancellationToken)
                .ConfigureAwait(false))
            .ToList();
        for (var index = 0; index < applyRunIds.Count; index++)
        {
            if (index >= pendingApplyRunIds.Count
                || pendingApplyRunIds[index] != applyRunIds[index])
            {
                return InvalidChain(
                    "The rollback chain is not in the required LIFO order.");
            }
        }

        return Result<IReadOnlyList<CodeGenerationRunRecord>>.Success(applies);
    }

    private async Task<Result<CodeGenerationRunRollbackResponse>> ExecuteNewRollbackAsync(
        Guid actorUserId,
        CodeGenerationRunRecord apply,
        Guid applyRunId,
        CancellationToken cancellationToken)
    {
        // 目标摘要在写盘前即可由检查点 PreviousManifest 确定；插入后再 Restore。
        GenerationRollbackCheckpoint checkpoint;
        try
        {
            checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(
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
                CodeGenerationSqlParameters.Create(
                    ("Id", runId),
                    ("TemplateId", (Guid?)null),
                    ("TemplateVersion", (long?)null),
                    ("SourceApplyRunId", applyRunId),
                    ("OperationKind", CodeGenerationRunOperationKinds.Rollback),
                    ("Status", CodeGenerationRunStatuses.Running),
                    ("ModuleKey", apply.ModuleKey),
                    ("EntityKey", apply.EntityKey),
                    ("SchemaSha256", apply.SchemaSha256),
                    ("ArtifactCount", targetManifest.Artifacts.Count),
                    ("ManifestSha256", manifestSha256),
                    ("ErrorCode", (string?)null),
                    ("RequestedByUserId", actorUserId),
                    ("StartedAtUtc", startedAtUtc),
                    ("FinishedAtUtc", startedAtUtc)),
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
                CodeGenerationSqlParameters.Create(
                    ("Id", runId),
                    ("FinishedAtUtc", clock.UtcNow)),
                CancellationToken.None)
            .ConfigureAwait(false);
        EnsureAffectedOne(completedRows, "completion");

        await gitWorkspace.PublishAsync(
                $"codegen(rollback): {apply.ModuleKey}/{apply.EntityKey} "
                + $"apply {applyRunId:N}",
                CancellationToken.None)
            .ConfigureAwait(false);

        await TryDeleteCheckpointAfterSucceededRollbackAsync(
                applyRunId,
                cancellationToken)
            .ConfigureAwait(false);

        var changedCount = plan.Actions.Count(action =>
            action.Kind != GenerationWriteActionKind.Unchanged);
        return Result<CodeGenerationRunRollbackResponse>.Success(
            ToResponse(
                runId,
                applyRunId,
                targetManifest.Artifacts.Count,
                changedCount,
                manifestSha256));
    }

    private static Result<IReadOnlyList<CodeGenerationRunRecord>> InvalidChain(
        string message) =>
        Result<IReadOnlyList<CodeGenerationRunRecord>>.Failure(
            new Error(
                CodeGenerationRunErrorCodes.InvalidRollbackChain,
                message,
                ErrorType.Validation));

    private static Result<CodeGenerationRunRollbackChainResponse> FailureChain(
        string code,
        string message,
        ErrorType type) =>
        Result<CodeGenerationRunRollbackChainResponse>.Failure(
            new Error(code, message, type));

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
                CodeGenerationSqlParameters.Create(
                    ("Id", runId),
                    ("ErrorCode", errorCode),
                    ("FinishedAtUtc", clock.UtcNow)),
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
