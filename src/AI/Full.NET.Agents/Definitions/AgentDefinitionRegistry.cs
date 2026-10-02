using System.Collections.Frozen;

namespace Full.NET.Agents.Definitions;

/// <summary>唯一可信定义目录；不支持运行时动态注册或模型指定工具。</summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加到末尾。</remarks>
public static class AgentDefinitionRegistry
{
    /// <summary>纯文本单轮代理定义键；不调用工具，直接返回模型文本输出，MaxToolIterations 为 0。</summary>
    public const string SingleTextKey = "fullnet-single-text-v1";

    /// <summary>只读工具循环代理定义键；仅允许 ping、模型列表与会话列表等只读工具，最多 8 轮迭代。</summary>
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
