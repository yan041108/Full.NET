using Full.NET.Abstractions.Time;
using Full.NET.Modules.Notifications.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationDeliveryWorkerOptionsTests
{
    [TestMethod]
    public void Validator_rejects_out_of_range_batch_poll_and_concurrency()
    {
        var validator = new NotificationDeliveryWorkerOptionsValidator();
        var invalid = validator.Validate(
            null,
            new NotificationDeliveryWorkerOptions
            {
                BatchSize = 0,
                PollMilliseconds = 10,
                MaxConcurrency = 32,
                LeaseSeconds = 1,
                MaxAttempts = 0,
                RetryBackoffMode = "linear",
            });

        Assert.IsTrue(invalid.Failed);
        StringAssert.Contains(invalid.FailureMessage, "BatchSize");
        StringAssert.Contains(invalid.FailureMessage, "PollMilliseconds");
        StringAssert.Contains(invalid.FailureMessage, "MaxConcurrency");
        StringAssert.Contains(invalid.FailureMessage, "RetryBackoffMode");
    }

    [TestMethod]
    public void Validator_accepts_default_options()
    {
        var validator = new NotificationDeliveryWorkerOptionsValidator();
        var result = validator.Validate(null, new NotificationDeliveryWorkerOptions());
        Assert.IsFalse(result.Failed);
    }
}

[TestClass]
public sealed class NotificationDeliveryHostedProcessorTests
{
    [TestMethod]
    public void GetDelayAfterBatch_is_zero_when_batch_is_full()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var options = new NotificationDeliveryWorkerOptions
        {
            BatchSize = 7,
            PollMilliseconds = 250,
        };
        var processor = new NotificationDeliveryHostedProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new SystemClock(),
            Options.Create(options),
            NullLogger<NotificationDeliveryHostedProcessor>.Instance);

        Assert.AreEqual(TimeSpan.Zero, processor.GetDelayAfterBatch(7));
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), processor.GetDelayAfterBatch(6));
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), processor.GetDelayAfterBatch(0));
    }
}
