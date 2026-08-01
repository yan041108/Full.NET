namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 基于已验证检查点规划工作区逆向写盘；不查询数据库，也不决定回滚资格。
/// </summary>
public static class GenerationRollbackWorkspace
{
    /// <summary>
    /// 要求当前磁盘 Manifest 与 AppliedManifest 逐字一致，并按检查点旧内容生成逆向计划。
    /// </summary>
    /// <remarks>
    /// PreviousManifest 为空时，目标是规范空 Manifest，表示当前无受管产物。
    /// </remarks>
    public static async Task<GenerationWritePlan> PlanAsync(
        string workspaceRoot,
        GenerationRollbackCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();

        var pathsToCapture = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in checkpoint.AppliedManifest.Artifacts)
        {
            pathsToCapture.Add(entry.RelativePath);
        }

        foreach (var relativePath in checkpoint.PreviousContents.Keys)
        {
            pathsToCapture.Add(relativePath);
        }

        var snapshot = await GenerationWorkspaceStore.CapturePathsAsync(
            workspaceRoot,
            pathsToCapture,
            cancellationToken);
        EnsureAppliedManifestAligned(snapshot, checkpoint);

        return GenerationWritePlanner.PlanFromDesiredContents(
            checkpoint.PreviousContents,
            snapshot.ExistingFiles,
            checkpoint.AppliedManifest);
    }

    /// <summary>
    /// 规划后仅在无冲突时复用 GenerationWorkspaceStore.ApplyAsync 执行逆向写盘。
    /// </summary>
    /// <remarks>
    /// 不删除检查点证据，也不修改任何数据库权威状态。
    /// </remarks>
    public static async Task<GenerationWritePlan> RestoreAsync(
        string workspaceRoot,
        GenerationRollbackCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        var plan = await PlanAsync(
            workspaceRoot,
            checkpoint,
            cancellationToken);
        if (!plan.CanApply)
        {
            return plan;
        }

        await GenerationWorkspaceStore.ApplyAsync(
            workspaceRoot,
            plan,
            cancellationToken);
        return plan;
    }

    private static void EnsureAppliedManifestAligned(
        GenerationWorkspaceSnapshot snapshot,
        GenerationRollbackCheckpoint checkpoint)
    {
        if (snapshot.PreviousManifest is null)
        {
            throw new GenerationWorkspaceConflictException(
                "当前工作区缺少生成清单，无法与检查点 AppliedManifest 对齐。",
                GenerationWorkspaceStore.ManifestRelativePath);
        }

        if (!StringComparer.Ordinal.Equals(
                snapshot.PreviousManifest.ToJson(),
                checkpoint.AppliedManifest.ToJson()))
        {
            throw new GenerationWorkspaceConflictException(
                "当前生成清单已偏离检查点 AppliedManifest，禁止逆向规划。",
                GenerationWorkspaceStore.ManifestRelativePath);
        }

        foreach (var entry in checkpoint.AppliedManifest.Artifacts)
        {
            if (!snapshot.ExistingFiles.TryGetValue(
                    entry.RelativePath,
                    out var content))
            {
                throw new GenerationWorkspaceConflictException(
                    "AppliedManifest 拥有的产物已缺失，禁止逆向规划。",
                    entry.RelativePath);
            }

            if (!StringComparer.Ordinal.Equals(
                    GenerationContentHash.Compute(content),
                    entry.Sha256))
            {
                throw new GenerationWorkspaceConflictException(
                    "AppliedManifest 拥有的产物摘要已漂移，禁止逆向规划。",
                    entry.RelativePath);
            }
        }
    }
}