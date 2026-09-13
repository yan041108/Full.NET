using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>请求作用域运行上下文；Coordinator 在领取运行后写入并在 finally 清理。</summary>
internal sealed class AgentRunExecutionContext : IAgentRunExecutionContext
{
    public SessionBindingSnapshot? Binding { get; private set; }

    public void SetBinding(SessionBindingSnapshot binding) => Binding = binding;

    public void Clear() => Binding = null;
}
