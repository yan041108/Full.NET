namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 不可通过 Broker 重试恢复的集成事件契约或安全失败；应直接转入 DLQ 或丢弃。
/// </summary>
public sealed class IntegrationEventPermanentException : Exception
{
    /// <summary>
    /// 使用分类失败结果构造永久异常。
    /// </summary>
    /// <param name="failure">已分类的失败详情。</param>
    public IntegrationEventPermanentException(IntegrationEventFailure failure)
        : base(failure.Summary)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }

    /// <summary>
    /// 导致本次永久失败的分类结果（含 Kind、Code 与摘要）。
    /// </summary>
    public IntegrationEventFailure Failure { get; }
}
