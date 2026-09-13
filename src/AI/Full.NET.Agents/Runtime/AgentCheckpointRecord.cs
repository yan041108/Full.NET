namespace Full.NET.Agents.Runtime;

/// <summary>已持久化的运行检查点；Payload 由协调器在恢复前校验版本与完整性。</summary>
public sealed record AgentCheckpointRecord(
    Guid CheckpointId,
    Guid RunId,
    long Sequence,
    int FormatVersion,
    string FrameworkVersion,
    int DefinitionVersion,
    string PayloadProtected,
    string Checksum);
