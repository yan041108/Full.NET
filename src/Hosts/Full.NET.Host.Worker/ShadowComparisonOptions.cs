using Microsoft.Extensions.Options;

namespace Full.NET.Host.Worker;

/// <summary>
/// Shadow topic comparison worker options; disabled by default and never binds business consumers.
/// </summary>
public sealed class ShadowComparisonOptions
{
    public const string SectionName = "Messaging:ShadowComparison";

    public bool Enabled { get; set; }

    public string TopicPrefix { get; set; } = "fullnet.dev.shadow";

    public string ConsumerGroup { get; set; } = "fullnet.messaging.shadow-comparison";

    public int PollTimeoutMilliseconds { get; set; } = 1_000;
}

internal sealed class ShadowComparisonOptionsValidator : IValidateOptions<ShadowComparisonOptions>
{
    public ValidateOptionsResult Validate(string? name, ShadowComparisonOptions options)
    {
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
