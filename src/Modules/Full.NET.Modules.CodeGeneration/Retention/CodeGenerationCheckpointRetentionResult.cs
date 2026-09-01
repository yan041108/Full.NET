namespace Full.NET.Modules.CodeGeneration.Retention;

/// <summary>
/// 保存一次检查点保留清理的扫描/删除/跳过/失败计数；<see cref="Empty"/> 表示未启用或无候选。
/// FAIL-closed：计数不一致（Scanned != Deleted + Skipped + Failed）时后续保留调度立即中止，避免目录泄漏。
/// </summary>
/// <param name="Scanned">扫描到的候选检查点目录总数。</param>
/// <param name="Deleted">成功删除的检查点目录数。</param>
/// <param name="Skipped">因仍在冷却窗口内而跳过的检查点目录数。</param>
/// <param name="Failed">尝试删除过程中出错的检查点目录数。</param>
internal sealed record CodeGenerationCheckpointRetentionResult(
    [property: System.ComponentModel.Description("扫描到的候选检查点目录总数；包含保留期内与过期的所有目录。")]
    int Scanned,
    [property: System.ComponentModel.Description("成功删除的检查点目录数；目录及其全部子项递归删除。")]
    int Deleted,
    [property: System.ComponentModel.Description("因仍在冷却窗口内而跳过的检查点目录数；对应选项 MinCheckpointAge。")]
    int Skipped,
    [property: System.ComponentModel.Description("尝试删除过程中出错的检查点目录数；失败目录将在下一轮保留调度中重试。")]
    int Failed)
{
    /// <summary>未启用或工作区未配置时返回的零值结果。</summary>
    public static CodeGenerationCheckpointRetentionResult Empty { get; } = new(0, 0, 0, 0);
}