using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>Worker 处理单次运行时的冻结会话绑定；API 请求路径保持为空。</summary>
internal interface IAgentRunExecutionContext
{
    SessionBindingSnapshot? Binding { get; }

    void SetBinding(SessionBindingSnapshot binding);

    void Clear();
}
