using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 将确定性 CRUD 生成、工作区捕获和安全写盘组合为单次应用流程。
/// </summary>
public static class CrudGenerationWorkspace
{
    /// <summary>将 owner/module/entity 所有权编码进工作区路径，避免同名 CLR 实体碰撞。</summary>
    public static string ArtifactPrefix(FullNetCrudSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return $"generated/{schema.OwnerKey}/{schema.ModuleKey}/{schema.EntityKey}/";
    }

    /// <summary>
    /// 只读取目标工作区并生成可审查的写盘计划。
    /// </summary>
    public static async Task<GenerationWritePlan> PlanAsync(
        string workspaceRoot,
        FullNetCrudSchema schema,
        CancellationToken cancellationToken = default) =>
        await PlanAsync(
            workspaceRoot,
            [schema],
            cancellationToken);

    /// <summary>
    /// 将多个已确认 Schema 的产物合并后统一规划，避免逐项规划误删同批次的其他产物。
    /// </summary>
    public static async Task<GenerationWritePlan> PlanAsync(
        string workspaceRoot,
        IReadOnlyList<FullNetCrudSchema> schemas,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        if (schemas.Count == 0)
        {
            throw new ArgumentException(
                "批量预览至少需要一个 CRUD Schema。",
                nameof(schemas));
        }

        var artifacts = schemas
            .SelectMany(schema =>
            {
                ArgumentNullException.ThrowIfNull(schema);
                return CrudArtifactGenerator.Generate(schema).Select(artifact => artifact with
                {
                    RelativePath = ArtifactPrefix(schema) + artifact.RelativePath,
                });
            })
            .ToArray();
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspaceRoot,
            artifacts,
            cancellationToken);
        var owners = schemas.Select(ArtifactPrefix).ToArray();
        bool IsOwned(GenerationManifestEntry entry) => owners.Any(prefix => entry.RelativePath.StartsWith(prefix, StringComparison.Ordinal));
        var ownedManifest = snapshot.PreviousManifest is null ? null : GenerationManifest.Create(
            snapshot.PreviousManifest.Artifacts.Where(IsOwned));
        var scoped = GenerationWritePlanner.Plan(
            artifacts,
            snapshot.ExistingFiles,
            ownedManifest);
        // 未参与本次生成的实体保留原有摘要；不得把人工漂移重新认领成生成结果。
        var next = scoped.NextManifest is null ? null : GenerationManifest.Create(
            (snapshot.PreviousManifest?.Artifacts.Where(entry => !IsOwned(entry)) ?? [])
            .Concat(scoped.NextManifest.Artifacts.Select(entry => entry with
            {
                Owner = schemas.Select(schema => new { Prefix = ArtifactPrefix(schema), Key = $"{schema.OwnerKey}/{schema.ModuleKey}/{schema.EntityKey}" })
                    .Single(owner => entry.RelativePath.StartsWith(owner.Prefix, StringComparison.Ordinal)).Key,
            })));
        return new GenerationWritePlan(scoped.Actions, snapshot.PreviousManifest, next);
    }

    /// <summary>
    /// 重新捕获并规划工作区；只有无冲突计划才允许进入安全写盘器。
    /// </summary>
    public static async Task<GenerationWritePlan> ApplyAsync(
        string workspaceRoot,
        FullNetCrudSchema schema,
        CancellationToken cancellationToken = default) =>
        await ApplyAsync(
            workspaceRoot,
            [schema],
            cancellationToken);

    /// <summary>
    /// 将多个 Schema 作为一个不可拆分批次规划；仅在整批无冲突时进入安全写盘器。
    /// </summary>
    public static async Task<GenerationWritePlan> ApplyAsync(
        string workspaceRoot,
        IReadOnlyList<FullNetCrudSchema> schemas,
        CancellationToken cancellationToken = default)
    {
        var plan = await PlanAsync(
            workspaceRoot,
            schemas,
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
}
