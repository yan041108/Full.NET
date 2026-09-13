namespace Full.NET.Agents.Definitions;

/// <summary>静态 Agent 定义；工具名称与副作用来自可信注册，模型不能扩展目录。</summary>
public sealed record AgentDefinition(
    string Key,
    int Version,
    int CheckpointFormatVersion,
    int MaxToolIterations,
    IReadOnlyList<string> AllowedToolNames);
