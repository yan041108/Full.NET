namespace Full.NET.Agents.Definitions;

/// <summary>静态 Agent 定义；工具名称与副作用来自可信注册，模型不能扩展目录。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Key 为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="Key">Agent 稳定机器码；发布后不可改名或删除。</param>
/// <param name="Version">Agent 定义版本；与 Key 共同标识不可变的行为契约。</param>
/// <param name="CheckpointFormatVersion">检查点序列化格式版本；用于恢复时兼容性判定。</param>
/// <param name="MaxToolIterations">单次运行允许的最大工具调用轮次；防止失控循环。</param>
/// <param name="AllowedToolNames">允许该 Agent 调用的工具稳定名称集合；空集合表示不允许任何工具。</param>
public sealed record AgentDefinition(
    string Key,
    int Version,
    int CheckpointFormatVersion,
    int MaxToolIterations,
    IReadOnlyList<string> AllowedToolNames);
