using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Persistence;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Full.NET.Modules.CodeGeneration.Git;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Retention;

/// <summary>
/// Worker 调用的检查点保留清理器；按保留期与容量上限分两阶段删除已回滚检查点目录，删除前必须验证工作区仍处于回滚后状态。
/// </summary>
internal sealed class CodeGenerationCheckpointRetentionRunner(
    IQueryExecutor queryExecutor,
    IOptions<CodeGenerationApplyOptions> applyOptions,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock,
    CodeGenerationApplyGate applyGate)
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
        if (!options.Enabled || !applyOptions.Value.Enabled || string.IsNullOrWhiteSpace(applyOptions.Value.WorkspaceRoot))
            return CodeGenerationCheckpointRetentionResult.Empty;
        if (!await applyGate.TryEnterAsync(cancellationToken).ConfigureAwait(false))
            return CodeGenerationCheckpointRetentionResult.Empty;
        try
        {
            if (await GenerationWorkspaceJournal.ReadPendingAsync(applyGate.Lease, cancellationToken).ConfigureAwait(false) is not null)
                return CodeGenerationCheckpointRetentionResult.Empty;
            return await RunCoreAsync(options, cancellationToken).ConfigureAwait(false);
        }
        finally { await applyGate.ReleaseAsync().ConfigureAwait(false); }
    }

    private async Task<CodeGenerationCheckpointRetentionResult> RunCoreAsync(
        CodeGenerationCheckpointRetentionOptions options, CancellationToken cancellationToken)
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
            parameters = CodeGenerationSqlParameters.Create(("Take", take));
        }
        else
        {
            statement = isMySql
                ? CodeGenerationRunSql.ListEligibleCheckpointCleanupMySql
                : CodeGenerationRunSql.ListEligibleCheckpointCleanupSqlServer;
            parameters = CodeGenerationSqlParameters.Create(
                ("CutoffUtc", cutoffUtc!.Value),
                ("Take", take));
        }

        return await queryExecutor
            .QueryAsync<CodeGenerationCheckpointCleanupCandidate>(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(
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
                if (!await CanReleaseEvidenceAsync(workspaceRoot, candidate, cancellationToken).ConfigureAwait(false))
                { skipped++; continue; }
                var checkpoint = await GenerationRollbackCheckpointStore.ReadMetadataAsync(
                    workspaceRoot,
                    candidate.ApplyRunId,
                    cancellationToken).ConfigureAwait(false);
                var currentManifest = await GenerationWorkspaceStore.ReadManifestOrEmptyAsync(
                    workspaceRoot, cancellationToken).ConfigureAwait(false);
                if (!WorkspaceMatchesPostRollbackState(
                        checkpoint,
                        currentManifest))
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

    private async Task<bool> CanReleaseEvidenceAsync(string root, CodeGenerationCheckpointCleanupCandidate candidate, CancellationToken token)
    {
        if (candidate.RollbackRunId == Guid.Empty) return false;
        var apply = await GenerationWorkspaceJournal.ReadOperationAsync(applyGate.Lease, candidate.ApplyRunId, token).ConfigureAwait(false);
        var rollback = await GenerationWorkspaceJournal.ReadOperationAsync(applyGate.Lease, candidate.RollbackRunId, token).ConfigureAwait(false);
        if (apply is not { Kind: "apply", Phase: "completed" } || rollback is not { Kind: "rollback", Phase: "completed" }
            || rollback.PreviousHead.TipOperationId != candidate.ApplyRunId) return false;
        var head = await GenerationWorkspaceJournal.ReadHeadAsync(applyGate.Lease, token).ConfigureAwait(false);
        var live = head.TipOperationId;
        var visited = new HashSet<Guid>();
        while (live is { } id)
        {
            if (id == candidate.ApplyRunId) return false;
            if (!visited.Add(id) || visited.Count > 8192) throw new IOException("回滚链损坏，禁止清理证据。");
            var operation = await GenerationWorkspaceJournal.ReadOperationAsync(applyGate.Lease, id, token).ConfigureAwait(false);
            if (operation is not { Kind: "apply", Phase: "completed" }) return false;
            live = operation.PreviousHead.TipOperationId;
        }
        foreach (var id in new[] { candidate.ApplyRunId, candidate.RollbackRunId })
        {
            var json = await GenerationWorkspaceMetadata.ReadAsync(root, $".fullnet/codegeneration-git-publications/{id:N}.json", token).ConfigureAwait(false);
            if (json is null) return false;
            var publication = CodeGenerationGitWorkspaceService.ParsePublication(json);
            if (publication.RunId != id || publication.Status is not ("disabled" or "local_only" or "published")) return false;
        }
        // 清单相同不代表人工正文未变；恢复后的新增、恢复与删除路径也必须保持原后态。
        var paths = rollback.Files.Where(file => file.RelativePath != GenerationWorkspaceStore.ManifestRelativePath).Select(file => file.RelativePath)
            .Concat(rollback.Checks.Select(check => check.RelativePath));
        var snapshot = await GenerationWorkspaceStore.CapturePathsAsync(root, paths, token).ConfigureAwait(false);
        foreach (var file in rollback.Files.Where(file => file.RelativePath != GenerationWorkspaceStore.ManifestRelativePath))
        {
            snapshot.ExistingFiles.TryGetValue(file.RelativePath, out var content);
            if (Hash(content) != Hash(file.After)) return false;
        }
        foreach (var check in rollback.Checks)
        {
            snapshot.ExistingFiles.TryGetValue(check.RelativePath, out var content);
            if (Hash(content) != check.Sha256) return false;
        }
        return true;
    }

    private static string? Hash(string? content) => content is null ? null : GenerationContentHash.Compute(content);

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
        GenerationRollbackCheckpointMetadata checkpoint,
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
