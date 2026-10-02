using System.Collections.Frozen;
using Full.NET.AI.Abstractions.Tools;

namespace Full.NET.Agents.Tools;

/// <summary>工具名称、版本、权限和副作用来自可信注册代码。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 PermissionCode、SideEffectKey 为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="Name">工具稳定名称；注册表内唯一，模型按此名称调用。</param>
/// <param name="Version">工具定义版本；与 Name 共同标识不可变的工具契约。</param>
/// <param name="PermissionCode">调用该工具所需的精确权限码。</param>
/// <param name="SideEffectKey">副作用稳定键；用于审计与回滚决策，可空表示只读无副作用。</param>
/// <param name="IsEnabled">该工具定义当前是否启用；禁用时注册仍保留但不可调用。</param>
/// <param name="Handler">工具处理器；为 <see langword="null"/> 表示仅元数据注册，不参与实际执行。</param>
public sealed record AgentToolDefinition(string Name, int Version, string PermissionCode, string SideEffectKey, bool IsEnabled, IAgentToolHandler? Handler);

/// <summary>静态显式注册；不扫描方法、Controller、服务或插件。</summary>
public sealed class AgentToolRegistry
{
    private readonly FrozenDictionary<string, AgentToolDefinition> tools;
    /// <summary>重复名称在创建时失败，不允许覆盖已有授权定义。</summary>
    /// <param name="definitions">可信来源提供的工具定义集合；重复 Name 将导致冻结字典构造失败。</param>
    public AgentToolRegistry(IEnumerable<AgentToolDefinition> definitions) =>
        tools = definitions.ToFrozenDictionary(item => item.Name, StringComparer.Ordinal);
    /// <summary>按精确稳定名称查找定义。</summary>
    /// <param name="name">工具稳定名称，使用序号（Ordinal）精确匹配。</param>
    /// <returns>匹配的工具定义；未找到时为 <see langword="null"/>。</returns>
    public AgentToolDefinition? Find(string name) => tools.GetValueOrDefault(name);
}
