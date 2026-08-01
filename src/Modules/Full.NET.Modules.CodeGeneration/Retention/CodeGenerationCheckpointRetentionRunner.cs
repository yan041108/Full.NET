using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Retention;

internal sealed class CodeGenerationCheckpointRetentionRunner(
    IQueryExecutor queryExecutor,
    IOptions<CodeGenerationApplyOptions> applyOptions,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock)
{
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

        var cutoffUtc = clock.UtcNow.AddDays(-options.RetentionDays);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? CodeGenerationRunSql.ListEligibleCheckpointCleanupMySql
            : CodeGenerationRunSql.ListEligibleCheckpointCleanupSqlServer;
        var candidates = await queryExecutor
            .QueryAsync<CodeGenerationCheckpointCleanupCandidate>(
                statement,
                new
                {
                    CutoffUtc = cutoffUtc,
                    Take = options.MaxDeletesPerRun,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var scanned = 0;
        var deleted = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var candidate in candidates)
        {
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

        return new CodeGenerationCheckpointRetentionResult(
            scanned,
            deleted,
            skipped,
            failed);
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