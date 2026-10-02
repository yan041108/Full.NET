using Full.NET.Agents.Framework;

namespace Full.NET.Agents.Runtime;

/// <summary>固定内部适配器版本，公共契约不暴露框架 SDK 类型。</summary>
/// <remarks>
/// 常量字符串发布后不可改名或删除；新增常量只能追加。
/// </remarks>
public static class AgentFrameworkRuntime
{
    /// <summary>Agent 框架运行时版本号；Checkpoint 恢复时据此判断框架兼容性，发布后不可改名。</summary>
    public const string FrameworkVersion = "1.20.0";

    /// <summary>创建绑定当前 FrameworkVersion 的 IAgentModelRunner 适配器；公共调用方不得直接依赖框架 SDK 类型。</summary>
    /// <returns>可在运行会话中执行模型调用的运行器实例。</returns>
    public static IAgentModelRunner CreateRunner() => new AgentFrameworkAdapter();
}
