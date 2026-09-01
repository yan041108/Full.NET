namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>
/// 数据库侧已满足 apply/rollback 成功且过冷却期的检查点清理候选。
/// 确定性：ApplyRunId 既是检查点目录名，也是数据库 Runs 表主键；两处不匹配时立即 FAIL-closed 跳过本次清理，避免误删。
/// </summary>
internal sealed class CodeGenerationCheckpointCleanupCandidate
{
    /// <summary>本次清理候选对应的成功 Apply/Rollback 运行 Id；与磁盘检查点目录名一致。</summary>
    public Guid ApplyRunId { get; init; }
}