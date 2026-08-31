using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Execution;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationDeliveryRetryTests
{
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
}
