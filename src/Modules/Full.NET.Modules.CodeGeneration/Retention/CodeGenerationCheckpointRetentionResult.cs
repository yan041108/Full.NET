namespace Full.NET.Modules.CodeGeneration.Retention;

/// <summary>
/// 保存一次检查点保留清理的扫描/删除/跳过/失败计数；<see cref="Empty"/> 表示未启用或无候选。
/// </summary>
internal sealed record CodeGenerationCheckpointRetentionResult(
    int Scanned,
    int Deleted,
    int Skipped,
    int Failed)
{
    /// <summary>未启用或工作区未配置时返回的零值结果。</summary>
    public static CodeGenerationCheckpointRetentionResult Empty { get; } = new(0, 0, 0, 0);
}