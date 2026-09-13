using Full.NET.Agents.Framework;

namespace Full.NET.Agents.Runtime;

/// <summary>固定内部适配器版本，公共契约不暴露框架 SDK 类型。</summary>
public static class AgentFrameworkRuntime
{
    public const string FrameworkVersion = "1.20.0";

    public static IAgentModelRunner CreateRunner() => new AgentFrameworkAdapter();
}
