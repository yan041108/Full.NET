using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 将确定性 CRUD 生成、工作区捕获和安全写盘组合为单次应用流程。
/// </summary>
public static class CrudGenerationWorkspace
{
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
                return CrudArtifactGenerator.Generate(schema);
            })
            .ToArray();
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspaceRoot,
            artifacts,
            cancellationToken);
        return GenerationWritePlanner.Plan(
            artifacts,
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
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
