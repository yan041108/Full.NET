using Microsoft.Extensions.Options;

namespace Full.NET.Host.Worker;

/// <summary>
/// Shadow topic comparison worker options; disabled by default and never binds business consumers.
/// </summary>
public sealed class ShadowComparisonOptions
{
    /// <summary>
    /// 配置节路径；用于从 IConfiguration 绑定 Shadow 对比选项，固定为 "Messaging:ShadowComparison"。
    /// </summary>
    public const string SectionName = "Messaging:ShadowComparison";

    /// <summary>
    /// 是否启用 Shadow Topic 对比；默认 false，启用后仅用于对比绝不绑定业务消费者。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Shadow Topic 名称前缀；默认 "fullnet.dev.shadow"，启用时不得为空。
    /// </summary>
    public string TopicPrefix { get; set; } = "fullnet.dev.shadow";

    /// <summary>
    /// Shadow 对比消费者组名；默认 "fullnet.messaging.shadow-comparison"，启用时不得为空。
    /// </summary>
    public string ConsumerGroup { get; set; } = "fullnet.messaging.shadow-comparison";

    /// <summary>
    /// 单次轮询超时时间（毫秒）；默认 1000，启用时不得小于 100。
    /// </summary>
    public int PollTimeoutMilliseconds { get; set; } = 1_000;

    /// <summary>单次完整观测最多保存的不同事件数；达到预算终止观测并标记不完整，禁止逐出后仍宣称完整去重。</summary>
    public int MaximumObservedEvents { get; set; } = 100_000;
}

internal sealed class ShadowComparisonOptionsValidator : IValidateOptions<ShadowComparisonOptions>
{
    public ValidateOptionsResult Validate(string? name, ShadowComparisonOptions options)
    {
        if (options.MaximumObservedEvents is < 1 or > 1_000_000)
            return ValidateOptionsResult.Fail("Messaging:ShadowComparison:MaximumObservedEvents must be between 1 and 1000000.");
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.TopicPrefix))
        {
            return ValidateOptionsResult.Fail(
                $"{ShadowComparisonOptions.SectionName}:TopicPrefix must be configured when shadow comparison is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.ConsumerGroup))
        {
            return ValidateOptionsResult.Fail(
                $"{ShadowComparisonOptions.SectionName}:ConsumerGroup must be configured when shadow comparison is enabled.");
        }

        if (options.PollTimeoutMilliseconds < 100)
        {
            return ValidateOptionsResult.Fail(
                $"{ShadowComparisonOptions.SectionName}:PollTimeoutMilliseconds must be at least 100.");
        }

        return ValidateOptionsResult.Success;
    }
}
