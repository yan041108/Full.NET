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

    /// <summary>受跨实例配额窗口阻塞；属于治理延迟而非厂商失败，保持可重试且不消耗尝试预算。</summary>
    public const string QuotaBlocked = "quota_blocked";

    /// <summary>排队期间进入静默时段；属于治理延迟而非厂商失败，保持可重试且不消耗尝试预算。</summary>
    public const string QuietHoursDeferred = "quiet_hours_deferred";

    /// <summary>某个配额维度缺少启用声明；失败关闭并终止，等待补齐配置后由人工重投。</summary>
    public const string QuotaMissing = "quota_missing";

    /// <summary>
    /// 判断失败类别是否允许自动换取新尝试。
    /// </summary>
    /// <param name="resultCategory">Provider 返回或本地推断的失败类别。</param>
    /// <param name="allowsAutomaticRetryAfterUnknown">
    /// 渠道是否具备幂等键或可信回执对账能力，从而可以安全地重试结果未知的调用。
    /// </param>
    /// <remarks>
    /// 结果未知默认不允许自动重试：只有能证明不会重复发送的渠道才由 Adapter 显式放开，
    /// 其余渠道必须暂挂等待回执对账或人工处置。
    /// </remarks>
    public static bool CanRetry(string resultCategory, bool allowsAutomaticRetryAfterUnknown = false) =>
        resultCategory is Transient or RateLimited
        || (resultCategory == Unknown && allowsAutomaticRetryAfterUnknown);

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
    /// 计算本次尝试序号。
    /// </summary>
    /// <param name="lastAttemptNumber">当前投递已记录的最大尝试序号；历史存在空档时也不得回退。</param>
    /// <param name="budgetUsed">本次领取后的独立预算计数，已排除完成的治理延迟。</param>
    /// <remarks>
    /// 取“最大历史序号 + 1”和预算的较大值以保持历史序号；自动重试上限另以预算判断。
    /// 人工重试只重置预算，不重置历史序号或租约世代。
    /// </remarks>
    public static int ResolveAttemptNumber(int lastAttemptNumber, long budgetUsed) =>
        Math.Max(checked(lastAttemptNumber + 1), (int)Math.Clamp(budgetUsed, 0, int.MaxValue));

    /// <summary>
    /// 将 Provider 结果映射为 Delivery 终态或下一次领取时间；耗尽次数后进入死信而不是无限重试。
    /// </summary>
    /// <param name="resultCategory">Provider 调用结果类别。</param>
    /// <param name="attemptNumber">本次已计入的尝试序号（从 1 开始）。</param>
    /// <param name="now">判定时间。</param>
    /// <param name="retryAfter">渠道建议的重试等待时间。</param>
    /// <param name="options">Worker 重试与预算配置。</param>
    /// <param name="allowsAutomaticRetryAfterUnknown">
    /// 渠道是否具备幂等键或可信回执对账能力；为 <see langword="false"/> 时结果未知暂挂为 <c>unknown</c>。
    /// </param>
    /// <param name="deferUntil">
    /// 静默时段等治理延迟的下一可发送时间；静默延迟必须直接使用按声明时区换算的结果，
    /// 不能按指数退避重算，否则会在静默窗口内反复领取。
    /// </param>
    public static (string Status, DateTimeOffset? NextAttempt) ResolveDeliveryOutcome(
        string resultCategory,
        int attemptNumber,
        DateTimeOffset now,
        TimeSpan? retryAfter,
        NotificationDeliveryWorkerOptions options,
        bool allowsAutomaticRetryAfterUnknown = false,
        DateTimeOffset? deferUntil = null)
    {
        if (resultCategory == Succeeded)
        {
            return ("sent", null);
        }

        // 静默时段是治理延迟：按声明时区把下一可发送时间写入 NextAttemptAtUtc，且不消耗厂商尝试预算。
        if (resultCategory == QuietHoursDeferred)
        {
            return deferUntil is { } allowedAt && allowedAt > now
                ? ("accepted", allowedAt)
                : ("accepted", now.AddSeconds(Math.Max(1, options.RetryDelaySeconds)));
        }

        // 配额阻塞是治理延迟：保持 accepted 并延后到下一可发送时间，且不消耗厂商尝试预算。
        if (resultCategory == QuotaBlocked)
        {
            return ("accepted", ComputeQuotaDeferral(now, retryAfter, options));
        }

        // 结果未知且渠道无法证明不重复发送时暂挂，等待可信回执对账或人工处置；
        // 这里不换取新尝试，也不进入死信，避免用自动重试制造重复发送。
        if (resultCategory == Unknown && !allowsAutomaticRetryAfterUnknown)
        {
            return ("unknown", null);
        }

        // 永久错误与缺失配额配置都必须失败关闭：直接终止，不进入死信等待人工重投。
        if (resultCategory is Permanent or QuotaMissing)
        {
            return ("failed", null);
        }

        if (attemptNumber >= options.MaxAttempts)
        {
            return ("dead_lettered", null);
        }

        if (!CanRetry(resultCategory, allowsAutomaticRetryAfterUnknown))
        {
            return ("failed", null);
        }

        return ("accepted", ComputeNextAttempt(now, attemptNumber, resultCategory, retryAfter, options));
    }

    /// <summary>计算配额阻塞的下一可发送时间；优先使用窗口重置剩余时间，并受最大退避上界约束。</summary>
    private static DateTimeOffset ComputeQuotaDeferral(
        DateTimeOffset now,
        TimeSpan? retryAfter,
        NotificationDeliveryWorkerOptions options)
    {
        var seconds = retryAfter is { } requested && requested > TimeSpan.Zero
            ? Math.Min(requested.TotalSeconds, options.RetryMaxDelaySeconds)
            : options.RetryDelaySeconds;
        return now.AddSeconds(Math.Max(1, seconds));
    }
}
