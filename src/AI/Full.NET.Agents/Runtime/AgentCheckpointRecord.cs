namespace Full.NET.Agents.Runtime;

/// <summary>已持久化的运行检查点；Payload 由协调器在恢复前校验版本与完整性。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="CheckpointId">检查点记录唯一标识。</param>
/// <param name="RunId">所属运行实例标识。</param>
/// <param name="Sequence">检查点在运行内的递增序号。</param>
/// <param name="FormatVersion">检查点载荷格式版本；恢复时据此选择反序列化器。</param>
/// <param name="FrameworkVersion">生成该检查点的框架版本字符串。</param>
/// <param name="DefinitionVersion">Agent 定义版本；与运行时定义不匹配时拒绝恢复。</param>
/// <param name="PayloadProtected">加密后的检查点载荷；明文不得落盘。</param>
/// <param name="Checksum">载荷完整性校验值；恢复时必须校验一致。</param>
public sealed record AgentCheckpointRecord(
    Guid CheckpointId,
    Guid RunId,
    long Sequence,
    int FormatVersion,
    string FrameworkVersion,
    int DefinitionVersion,
    string PayloadProtected,
    string Checksum);
