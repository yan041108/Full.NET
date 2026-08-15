namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 顺序执行多个 Driver 预检步骤；任一步失败即关闭本次容量执行。
/// </summary>
public sealed class KafkaCapacityChainedPreflight(
    params IKafkaCapacityDriverPreflight[] steps) : IKafkaCapacityDriverPreflight
{
    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            ArgumentNullException.ThrowIfNull(step);
            await step.ValidateAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
