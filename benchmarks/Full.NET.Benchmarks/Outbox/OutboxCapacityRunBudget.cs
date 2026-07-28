namespace Full.NET.Benchmarks.Outbox;

/// <summary>
/// 跟踪单次 Outbox 容量命令允许新增的样本数量。
/// </summary>
/// <remarks>
/// 只有已经写入 checkpoint 的新样本才应调用 <see cref="RecordCompletedSample"/>；
/// 从旧 checkpoint 跳过的完成键不占用本次运行预算。
/// </remarks>
public sealed class OutboxCapacityRunBudget
{
    private readonly int _maximumNewSamples;

    /// <summary>
    /// 初始化单次运行预算，零表示不限制新增样本数量。
    /// </summary>
    public OutboxCapacityRunBudget(int maximumNewSamples)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumNewSamples);
        _maximumNewSamples = maximumNewSamples;
    }

    /// <summary>
    /// 获取本次命令已经持久化的新样本数量。
    /// </summary>
    public int CompletedSamples { get; private set; }

    /// <summary>
    /// 获取有限预算是否已经耗尽；无限预算始终为 false。
    /// </summary>
    public bool IsExhausted =>
        _maximumNewSamples > 0
        && CompletedSamples >= _maximumNewSamples;

    /// <summary>
    /// 记录一个已持久化的新样本，并返回本次命令是否应正常停止。
    /// </summary>
    public bool RecordCompletedSample()
    {
        CompletedSamples = checked(CompletedSamples + 1);
        return IsExhausted;
    }
}
