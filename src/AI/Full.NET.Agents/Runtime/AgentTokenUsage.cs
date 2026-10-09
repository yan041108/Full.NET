namespace Full.NET.Agents.Runtime;

/// <summary>两个运行消费者共享计量规则；未知是不可补齐的总量，首次调用前的空值只表示尚未计量。</summary>
internal static class AgentTokenUsage
{
    internal static long? Add(long? current, long? delta, bool firstModelCall)
    {
        if (current is < 0 || delta is < 0)
            throw new InvalidOperationException("Agent token usage must be nonnegative.");
        if (firstModelCall) return delta;
        if (current is null || delta is null) return null;
        // 超出计量类型时失败关闭，不能回绕成负数或伪造较低预算用量。
        return checked(current.Value + delta.Value);
    }
}
