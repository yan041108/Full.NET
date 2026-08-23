using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Retention;

/// <summary>
/// Worker 调用的检查点保留清理器；按保留期与容量上限分两阶段删除已回滚检查点目录，删除前必须验证工作区仍处于回滚后状态。
/// </summary>
internal sealed class CodeGenerationCheckpointRetentionRunner(
    IQueryExecutor queryExecutor,
    IOptions<CodeGenerationApplyOptions> applyOptions,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock)
{
    /// <summary>
    /// 执行一次清理：先按 RetentionDays 删除过冷却期的检查点，再按 MaxCheckpointCount 处理容量溢出；两阶段共享 MaxDeletesPerRun 预算并按 ApplyRunId 去重。
    /// 删除前重新捕获工作区 Manifest 并与检查点 PreviousManifest 比对，不一致则跳过，避免误删仍需回滚的检查点。
    /// </summary>
    public async Task<CodeGenerationCheckpointRetentionResult> RunOnceAsync(
        CodeGenerationCheckpointRetentionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled || !applyOptions.Value.Enabled)
        {
            return CodeGenerationCheckpointRetentionResult.Empty;
        }

        var workspaceRoot = applyOptions.Value.WorkspaceRoot;
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return CodeGenerationCheckpointRetentionResult.Empty;
        }

        var scanned = 0;
        var deleted = 0;
        var skipped = 0;
        var failed = 0;
        var remainingBudget = options.MaxDeletesPerRun;
        var processedApplyRunIds = new HashSet<Guid>();
        if (remainingBudget > 0)
        {
            var cutoffUtc = clock.UtcNow.AddDays(-options.RetentionDays);
            var retentionCandidates = await QueryCandidatesAsync(
                    isMySql: databaseOptions.Value.Provider == DatabaseProvider.MySql,
                    useCapacityOverflow: false,
                    cutoffUtc,
                    remainingBudget,
                    cancellationToken)
                .ConfigureAwait(false);
            (scanned, deleted, skipped, failed, remainingBudget) =
                await ProcessCandidatesAsync(
                        workspaceRoot,
                        retentionCandidates,
                        processedApplyRunIds,
                        scanned,
                        deleted,
                        skipped,
                        failed,
                        remainingBudget,
                        cancellationToken)
                    .ConfigureAwait(false);
        }

        if (options.MaxCheckpointCount > 0 && remainingBudget > 0)
        {
            var checkpointCount = CountCheckpointDirectories(workspaceRoot);
            var excess = checkpointCount - options.MaxCheckpointCount;
            if (excess > 0)
            {
                var take = Math.Min(excess, remainingBudget);
                var overflowCandidates = await QueryCandidatesAsync(
                        isMySql: databaseOptions.Value.Provider == DatabaseProvider.MySql,
                        useCapacityOverflow: true,
                        cutoffUtc: null,
                        take,
                        cancellationToken)
                    .ConfigureAwait(false);
                (scanned, deleted, skipped, failed, remainingBudget) =
                    await ProcessCandidatesAsync(
                            workspaceRoot,
                            overflowCandidates,
                            processedApplyRunIds,
                            scanned,
                            deleted,
                            skipped,
                            failed,
                            remainingBudget,
                            cancellationToken)
                        .ConfigureAwait(false);
            }
        }

        return new CodeGenerationCheckpointRetentionResult(
            scanned,
            deleted,
            skipped,
            failed);
    }

    private async Task<IReadOnlyList<CodeGenerationCheckpointCleanupCandidate>>
        QueryCandidatesAsync(
            bool isMySql,
            bool useCapacityOverflow,
            DateTimeOffset? cutoffUtc,
            int take,
            CancellationToken cancellationToken)
    {
        SqlStatement statement;
        object parameters;
        if (useCapacityOverflow)
        {
            statement = isMySql
                ? CodeGenerationRunSql.ListCapacityOverflowCheckpointCleanupMySql
                : CodeGenerationRunSql.ListCapacityOverflowCheckpointCleanupSqlServer;
            parameters = new { Take = take };
        }
        else
        {
            statement = isMySql
                ? CodeGenerationRunSql.ListEligibleCheckpointCleanupMySql
                : CodeGenerationRunSql.ListEligibleCheckpointCleanupSqlServer;
            parameters = new
            {
                CutoffUtc = cutoffUtc!.Value,
                Take = take,
            };
        }

        return await queryExecutor
            .QueryAsync<CodeGenerationCheckpointCleanupCandidate>(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<(
        int Scanned,
        int Deleted,
        int Skipped,
        int Failed,
        int RemainingBudget)> ProcessCandidatesAsync(
        string workspaceRoot,
        IReadOnlyList<CodeGenerationCheckpointCleanupCandidate> candidates,
        ISet<Guid> processedApplyRunIds,
        int scanned,
        int deleted,
        int skipped,
        int failed,
        int remainingBudget,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates)
        {
            if (remainingBudget <= 0)
            {
                break;
            }

            if (!processedApplyRunIds.Add(candidate.ApplyRunId))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            scanned++;
            try
            {
                var checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(
                    workspaceRoot,
                    candidate.ApplyRunId,
                    cancellationToken).ConfigureAwait(false);
                var snapshot = await GenerationWorkspaceStore.CaptureAsync(
                    workspaceRoot,
                    Array.Empty<GeneratedArtifact>(),
                    cancellationToken).ConfigureAwait(false);
                if (!WorkspaceMatchesPostRollbackState(
                        checkpoint,
                        snapshot.PreviousManifest))
                {
                    skipped++;
                    continue;
                }

                if (await GenerationRollbackCheckpointStore.TryDeleteAsync(
                        workspaceRoot,
                        candidate.ApplyRunId,
                        cancellationToken).ConfigureAwait(false))
                {
                    deleted++;
                    remainingBudget--;
                }
                else
                {
                    skipped++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failed++;
            }
        }

        return (scanned, deleted, skipped, failed, remainingBudget);
    }

    private static int CountCheckpointDirectories(string workspaceRoot)
    {
        var checkpointRoot = Path.Combine(
            workspaceRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar),
            GenerationRollbackCheckpointStore.RootRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        if (!Directory.Exists(checkpointRoot))
        {
            return 0;
        }

        return Directory.EnumerateDirectories(checkpointRoot).Count();
    }

    private static bool WorkspaceMatchesPostRollbackState(
        GenerationRollbackCheckpoint checkpoint,
        GenerationManifest? currentManifest)
    {
        var expected = checkpoint.PreviousManifest;
        if (expected is null)
        {
            return currentManifest is null
                || currentManifest.Artifacts.Count == 0;
        }

        if (currentManifest is null)
        {
            return false;
        }

        return string.Equals(
            expected.ToJson(),
            currentManifest.ToJson(),
            StringComparison.Ordinal);
    }
}
