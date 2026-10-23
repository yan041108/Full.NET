using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Execution;

namespace Full.NET.UnitTests.Notifications;

/// <summary>单元测试（通知）：Notification Delivery Retry。</summary>
[TestClass]
public sealed class NotificationDeliveryRetryTests
{
    [TestMethod]
    public void Quiet_deferral_uses_the_declared_end_even_when_retry_budget_is_exhausted()
    {
        var now = new DateTimeOffset(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);
        var end = now.AddHours(9);
        var result = NotificationDeliveryRetry.ResolveDeliveryOutcome(
            NotificationDeliveryRetry.QuietHoursDeferred, 99, now, null,
            new NotificationDeliveryWorkerOptions { MaxAttempts = 3, RetryMaxDelaySeconds = 60 },
            deferUntil: end);
        Assert.AreEqual("accepted", result.Status);
        Assert.AreEqual(end, result.NextAttempt);
    }

    [TestMethod]
    public void Exponential_backoff_caps_at_max_delay()
    {
        var options = new NotificationDeliveryWorkerOptions
        {
            RetryDelaySeconds = 2,
            RetryBackoffMode = "exponential",
            RetryMaxDelaySeconds = 10,
            MaxAttempts = 8,
        };
        var now = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

        var first = NotificationDeliveryRetry.ComputeNextAttempt(
            now,
            1,
            NotificationDeliveryRetry.Transient,
            null,
            options);
        var capped = NotificationDeliveryRetry.ComputeNextAttempt(
            now,
            8,
            NotificationDeliveryRetry.Transient,
            null,
            options);

        Assert.AreEqual(now.AddSeconds(2), first);
        Assert.AreEqual(now.AddSeconds(10), capped);
    }

    [TestMethod]
    public void Rate_limited_retry_after_is_honored_and_capped()
    {
        var options = new NotificationDeliveryWorkerOptions
        {
            RetryDelaySeconds = 2,
            RetryMaxDelaySeconds = 15,
        };
        var now = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

        var honored = NotificationDeliveryRetry.ComputeNextAttempt(
            now,
            1,
            NotificationDeliveryRetry.RateLimited,
            TimeSpan.FromSeconds(9),
            options);
        var capped = NotificationDeliveryRetry.ComputeNextAttempt(
            now,
            1,
            NotificationDeliveryRetry.RateLimited,
            TimeSpan.FromSeconds(90),
            options);

        Assert.AreEqual(now.AddSeconds(9), honored);
        Assert.AreEqual(now.AddSeconds(15), capped);
    }

    [TestMethod]
    public void Permanent_fails_immediately_and_exhausted_attempts_dead_letter()
    {
        var options = new NotificationDeliveryWorkerOptions { MaxAttempts = 3 };
        var now = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

        var permanent = NotificationDeliveryRetry.ResolveDeliveryOutcome(
            NotificationDeliveryRetry.Permanent,
            1,
            now,
            null,
            options);
        var deadLetter = NotificationDeliveryRetry.ResolveDeliveryOutcome(
            NotificationDeliveryRetry.Transient,
            3,
            now,
            null,
            options);
        var retry = NotificationDeliveryRetry.ResolveDeliveryOutcome(
            NotificationDeliveryRetry.Transient,
            1,
            now,
            null,
            options);

        Assert.AreEqual("failed", permanent.Status);
        Assert.IsNull(permanent.NextAttempt);
        Assert.AreEqual("dead_lettered", deadLetter.Status);
        Assert.AreEqual("accepted", retry.Status);
        Assert.IsNotNull(retry.NextAttempt);
    }

    [TestMethod]
    public void Unknown_result_parks_without_automatic_retry_when_channel_is_not_idempotent()
    {
        var options = new NotificationDeliveryWorkerOptions { MaxAttempts = 3, RetryDelaySeconds = 2 };
        var now = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

        var parked = NotificationDeliveryRetry.ResolveDeliveryOutcome(
            NotificationDeliveryRetry.Unknown,
            1,
            now,
            null,
            options,
            allowsAutomaticRetryAfterUnknown: false);
        // 即使尚未耗尽预算也不能自动换取新尝试，避免非幂等渠道重复发送。
        var parkedAtBudgetEdge = NotificationDeliveryRetry.ResolveDeliveryOutcome(
            NotificationDeliveryRetry.Unknown,
            3,
            now,
            null,
            options,
            allowsAutomaticRetryAfterUnknown: false);

        Assert.AreEqual("unknown", parked.Status);
        Assert.IsNull(parked.NextAttempt);
        Assert.AreEqual("unknown", parkedAtBudgetEdge.Status);
        Assert.IsNull(parkedAtBudgetEdge.NextAttempt);
        Assert.IsFalse(NotificationDeliveryRetry.CanRetry(NotificationDeliveryRetry.Unknown));
    }

    [TestMethod]
    public void Unknown_result_retries_with_budget_when_channel_proves_idempotency()
    {
        var options = new NotificationDeliveryWorkerOptions { MaxAttempts = 3, RetryDelaySeconds = 2 };
        var now = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

        var retry = NotificationDeliveryRetry.ResolveDeliveryOutcome(
            NotificationDeliveryRetry.Unknown,
            1,
            now,
            null,
            options,
            allowsAutomaticRetryAfterUnknown: true);
        var exhausted = NotificationDeliveryRetry.ResolveDeliveryOutcome(
            NotificationDeliveryRetry.Unknown,
            3,
            now,
            null,
            options,
            allowsAutomaticRetryAfterUnknown: true);

        Assert.AreEqual("accepted", retry.Status);
        Assert.IsNotNull(retry.NextAttempt);
        Assert.AreEqual("dead_lettered", exhausted.Status);
        Assert.IsNull(exhausted.NextAttempt);
        Assert.IsTrue(NotificationDeliveryRetry.CanRetry(NotificationDeliveryRetry.Unknown, true));
    }

    [TestMethod]
    public void Attempt_number_counts_claims_abandoned_before_a_result_is_written()
    {
        // 首次领取尚无记录，预算为 1。
        Assert.AreEqual(1, NotificationDeliveryRetry.ResolveAttemptNumber(0, 1));
        Assert.AreEqual(2, NotificationDeliveryRetry.ResolveAttemptNumber(1, 2));
        // 中断领取保留预算，序号跟随已消耗预算。
        Assert.AreEqual(3, NotificationDeliveryRetry.ResolveAttemptNumber(1, 3));
        // 人工重试只重置预算；历史记录序号不能回退。
        Assert.AreEqual(2, NotificationDeliveryRetry.ResolveAttemptNumber(1, 1));
        // 历史只有一行但序号为 2，人工重投必须使用 3，不能按行数生成重复序号。
        Assert.AreEqual(3, NotificationDeliveryRetry.ResolveAttemptNumber(2, 1));
        // 多次完成的治理延迟退还预算，首次外发仍使用序号 1。
        Assert.AreEqual(1, NotificationDeliveryRetry.ResolveAttemptNumber(0, 1));
    }
}
