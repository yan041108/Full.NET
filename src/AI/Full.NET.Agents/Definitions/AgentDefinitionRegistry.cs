using System.Collections.Frozen;

namespace Full.NET.Agents.Definitions;

/// <summary>唯一可信定义目录；不支持运行时动态注册或模型指定工具。</summary>
public static class AgentDefinitionRegistry
{
    public const string SingleTextKey = "fullnet-single-text-v1";
    public const string ReadOnlyToolLoopKey = "fullnet-readonly-tool-loop-v1";

    private static readonly FrozenDictionary<(string Key, int Version), AgentDefinition> Definitions =
        new Dictionary<(string, int), AgentDefinition>
        {
            [(SingleTextKey, 1)] = new(
                SingleTextKey,
                1,
                CheckpointFormatVersion: 1,
                MaxToolIterations: 0,
                AllowedToolNames: []),
            [(ReadOnlyToolLoopKey, 1)] = new(
                ReadOnlyToolLoopKey,
                1,
                CheckpointFormatVersion: 1,
                MaxToolIterations: 8,
                AllowedToolNames: ["ai.tools.ping", "ai.models.list", "ai.chat.sessions.list"]),
        }.ToFrozenDictionary();

    public static AgentDefinition? Resolve(string key, int version) =>
        Definitions.GetValueOrDefault((key, version));
}
