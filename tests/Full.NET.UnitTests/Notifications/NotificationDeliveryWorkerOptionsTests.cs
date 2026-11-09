using System.Diagnostics;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Notifications.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>单元测试（通知）：Notification Delivery Worker Options。</summary>
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

/// <summary>单元测试：NotificationDeliveryHostedProcessor。</summary>
[TestClass]
public sealed class NotificationDeliveryHostedProcessorTests
{
    [TestMethod]
    [DataRow(ServiceCapacityFailureKind.Rejected)]
    [DataRow(ServiceCapacityFailureKind.Timeout)]
    public async Task Capacity_pressure_defers_polling_and_keeps_other_failures_visible(ServiceCapacityFailureKind kind)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var token = budget.Token;
        var scopes = Substitute.For<IServiceScopeFactory>();
        var retry = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unexpected = new InvalidOperationException("受控通知故障");
        long firstAttempt = 0;
        var attempts = 0;
        scopes.CreateScope().Returns(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstAttempt = Stopwatch.GetTimestamp();
                throw new ServiceCapacityExceededException(kind, TimeSpan.FromMilliseconds(500));
            }
            retry.TrySetResult(Stopwatch.GetElapsedTime(firstAttempt));
            throw unexpected;
        });
        var logger = new PollLog();
        using var processor = new NotificationDeliveryHostedProcessor(scopes, new SystemClock(),
            Options.Create(new NotificationDeliveryWorkerOptions { PollMilliseconds = 100 }), logger);
        await processor.StartAsync(token);
        try
        {
            var first = await logger.First.Task.WaitAsync(token);
            Assert.AreEqual(LogLevel.Warning, first.Level);
            StringAssert.Contains(first.Message, kind.ToString());
            Assert.IsNull(first.Exception);
            Assert.IsTrue(await retry.Task.WaitAsync(token) >= TimeSpan.FromMilliseconds(490));
            var next = await logger.Next.Task.WaitAsync(token);
            Assert.AreEqual(LogLevel.Error, next.Level);
            Assert.AreSame(unexpected, next.Exception);
        }
        finally
        {
            await processor.StopAsync(token);
        }
    }

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

    private sealed class PollLog : ILogger<NotificationDeliveryHostedProcessor>
    {
        internal TaskCompletionSource<Entry> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<Entry> Next { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var entry = new Entry(logLevel, formatter(state, exception), exception);
            if (!First.TrySetResult(entry)) Next.TrySetResult(entry);
        }
    }

    private sealed record Entry(LogLevel Level, string Message, Exception? Exception);
}
