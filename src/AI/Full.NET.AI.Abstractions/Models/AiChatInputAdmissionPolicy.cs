using Full.NET.AI.Abstractions.Budgets;

namespace Full.NET.AI.Abstractions.Models;

/// <summary>宿主对精确 Chat 输入硬限的要求；估算、回执或字符上限不能认证请求前 Token 数。</summary>
/// <param name="requireExactTokens">是否强制派发前精确计量。</param>
public sealed class AiChatInputAdmissionPolicy(bool requireExactTokens = false)
{
    /// <summary>兼容已有预算预留；仍保留有界输入快照和派发后回执检查。</summary>
    public static AiChatInputAdmissionPolicy Default { get; } = new();

    /// <summary>宿主是否要求派发前的精确输入计量。</summary>
    public bool RequireExactTokens { get; } = requireExactTokens;

    /// <summary>当前三个 Chat 传输均无经认证的精确请求计量；严格模式必须拒绝，不能降级到估算。</summary>
    public void RequireChatDispatch()
    {
        if (RequireExactTokens) throw new AiBudgetException("ai.budget.hard_input_unavailable");
    }
}
