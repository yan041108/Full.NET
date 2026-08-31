using Full.NET.Modules.Notifications.Execution;

namespace Full.NET.Modules.Notifications.Domain;

/// <summary>失败类别与有界退避；永久错误不得重试或换厂商。</summary>
internal static class NotificationDeliveryRetry
{
    public const string Succeeded = "succeeded";
    public const string Transient = "transient";
    public const string RateLimited = "rate_limited";
    public const string Permanent = "permanent";
    public const string Unknown = "unknown";

    public static bool CanRetry(string resultCategory) =>
        resultCategory is Transient or RateLimited or Unknown;

    public static DateTimeOffset ComputeNextAttempt(
        DateTimeOffset now,
        int attemptNumber,
        string resultCategory,
        TimeSpan? retryAfter,
        NotificationDeliveryWorkerOptions options)
    {
        if (string.Equals(resultCategory, RateLimited, StringComparison.Ordinal)
            && retryAfter is { } requested
            && requested > TimeSpan.Zero)
        {
            var capped = Math.Min(requested.TotalSeconds, options.RetryMaxDelaySeconds);
            return now.AddSeconds(Math.Max(1, capped));
        }

        var baseSeconds = options.RetryDelaySeconds;
        var delaySeconds = string.Equals(options.RetryBackoffMode, "exponential", StringComparison.Ordinal)
            ? baseSeconds * Math.Pow(2, Math.Max(attemptNumber - 1, 0))
            : baseSeconds;
        delaySeconds = Math.Min(delaySeconds, options.RetryMaxDelaySeconds);
        return now.AddSeconds(Math.Max(1, delaySeconds));
    }

    /// <summary>
    /// 将 Provider 结果映射为 Delivery 终态或下一次领取时间；耗尽次数后进入死信而不是无限重试。
    /// </summary>
    public static (string Status, DateTimeOffset? NextAttempt) ResolveDeliveryOutcome(
        string resultCategory,
        int attemptNumber,
        DateTimeOffset now,
        TimeSpan? retryAfter,
        NotificationDeliveryWorkerOptions options)
    {
        if (resultCategory == Succeeded)
        {
            return ("sent", null);
        }

        if (resultCategory == Permanent
            || attemptNumber >= options.MaxAttempts)
        {
            return (attemptNumber >= options.MaxAttempts && resultCategory != Permanent
                ? "dead_lettered"
                : "failed", null);
        }

        if (!CanRetry(resultCategory))
        {
            return ("failed", null);
        }

        return ("accepted", ComputeNextAttempt(now, attemptNumber, resultCategory, retryAfter, options));
    }
}
