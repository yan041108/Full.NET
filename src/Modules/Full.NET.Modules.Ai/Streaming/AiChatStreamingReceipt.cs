using Full.NET.AI.Abstractions.Budgets;

namespace Full.NET.Modules.Ai.Streaming;

/// <summary>聊天派发与完整消费的内部回执；部分流不证明完整计量，释放或落库失败不能抹掉完整报告。</summary>
internal sealed class AiChatStreamingReceipt
{
    private AiOperationUsage usage = new(0, 0);
    private AiOperationUsage reported = new(null, null);

    /// <summary>可能计费调用之前切换为未知；工厂、传输启动及派发前取消保持确定零。</summary>
    internal void Begin() => usage = new(null, null);

    /// <summary>保留原聊天最近已知累计回执语义；不把缺失方向补零，不信任可变SDK对象。</summary>
    internal void Report(AiOperationUsage value)
    {
        // 新输入总量没有缓存报告时不沿用旧快照的优惠；同一输入的分方向报告仍可合并。
        var cached = value.CachedInputTokens ?? (value.InputTokens is null || value.InputTokens == reported.InputTokens
            ? reported.CachedInputTokens : null);
        var candidate = new AiOperationUsage(value.InputTokens ?? reported.InputTokens,
            value.OutputTokens ?? reported.OutputTokens, cached);
        AiExecutionBudget.ValidateUsage(candidate); reported = candidate;
    }

    /// <summary>完整枚举已结束才确认报告；中途断流、输出失败或取消仍保留未知消费。</summary>
    internal void Complete() => usage = reported;

    /// <summary>只返回不可变回执；不拥有客户端、事务、预算凭据或取消源。</summary>
    internal AiOperationUsage Freeze() => usage;
}
