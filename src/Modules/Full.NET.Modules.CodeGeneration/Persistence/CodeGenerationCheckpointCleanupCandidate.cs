namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>
/// 数据库侧已满足 apply/rollback 成功且过冷却期的检查点清理候选。
/// </summary>
internal sealed class CodeGenerationCheckpointCleanupCandidate
{
    public Guid ApplyRunId { get; init; }
}