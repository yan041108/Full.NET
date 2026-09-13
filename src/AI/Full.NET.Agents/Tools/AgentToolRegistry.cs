using System.Collections.Frozen;
using Full.NET.AI.Abstractions.Tools;

namespace Full.NET.Agents.Tools;

/// <summary>工具名称、版本、权限和副作用来自可信注册代码。</summary>
public sealed record AgentToolDefinition(string Name, int Version, string PermissionCode, string SideEffectKey, bool IsEnabled, IAgentToolHandler? Handler);

/// <summary>静态显式注册；不扫描方法、Controller、服务或插件。</summary>
public sealed class AgentToolRegistry
{
    private readonly FrozenDictionary<string, AgentToolDefinition> tools;
    /// <summary>重复名称在创建时失败，不允许覆盖已有授权定义。</summary>
    public AgentToolRegistry(IEnumerable<AgentToolDefinition> definitions) =>
        tools = definitions.ToFrozenDictionary(item => item.Name, StringComparer.Ordinal);
    /// <summary>按精确稳定名称查找定义。</summary>
    public AgentToolDefinition? Find(string name) => tools.GetValueOrDefault(name);
}
