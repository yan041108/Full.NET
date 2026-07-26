using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Full.NET.Hosting.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.Async;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
[DoNotParallelize]
public sealed class HighPriorityLoggingTests
{
    [TestMethod]
    public void General_queue_saturation_does_not_delay_error_delivery()
    {
        using var monitors = new FullNetLoggingMonitors();
        using var generalSink = new BlockingSink();
        var highPrioritySink = new CollectingSink();
        using var logger = CreateLogger(
            monitors,
            generalSink,
            highPrioritySink,
            generalBufferSize: 1,
            highPriorityBufferSize: 4);

        logger.Information("block general channel");
        Assert.IsTrue(generalSink.WaitUntilEntered());
        for (var index = 0; index < 128; index++)
        {
            logger.Information("general event {Index}", index);
        }

        Assert.IsTrue(SpinWait.SpinUntil(
            () => monitors.General.Snapshot.DroppedMessagesCount > 0,
            TimeSpan.FromSeconds(2)));

        logger.Error("must use independent channel");

        Assert.IsTrue(highPrioritySink.WaitForCount(1));
        Assert.AreEqual(
            "must use independent channel",
            highPrioritySink.Events.Single().RenderMessage());
        generalSink.Release();
    }

    [TestMethod]
    public void High_priority_queue_saturation_never_blocks_callers()
    {
        using var monitors = new FullNetLoggingMonitors();
        var generalSink = new CollectingSink();
        using var highPrioritySink = new BlockingSink();
        using var logger = CreateLogger(
            monitors,
            generalSink,
            highPrioritySink,
            generalBufferSize: 4,
            highPriorityBufferSize: 1);

        logger.Error("block high priority channel");
        Assert.IsTrue(highPrioritySink.WaitUntilEntered());
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 256; index++)
        {
            logger.Error("high priority event {Index}", index);
        }

        stopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromSeconds(2), stopwatch.Elapsed);
        Assert.IsTrue(SpinWait.SpinUntil(
            () => monitors.HighPriority.Snapshot.DroppedMessagesCount > 0,
            TimeSpan.FromSeconds(2)));
        highPrioritySink.Release();
    }

    [TestMethod]
    public void General_worker_continues_after_one_sink_failure()
    {
        using var monitors = new FullNetLoggingMonitors();
        var delivered = new CollectingSink();
        var generalSink = new ThrowOnceSink(delivered);
        using var logger = CreateLogger(
            monitors,
            generalSink,
            new CollectingSink(),
            generalBufferSize: 4,
            highPriorityBufferSize: 4);

        logger.Information("discard first general event");
        Assert.IsTrue(generalSink.WaitUntilAttempted());
        logger.Information("deliver second general event");

        Assert.IsTrue(delivered.WaitForCount(1));
        Assert.AreEqual(
            "deliver second general event",
            delivered.Events.Single().RenderMessage());
        Assert.AreEqual(1, monitors.General.Snapshot.DroppedMessagesCount);
    }

    [TestMethod]
    public void High_priority_worker_continues_after_one_sink_failure()
    {
        using var monitors = new FullNetLoggingMonitors();
        var delivered = new CollectingSink();
        var highPrioritySink = new ThrowOnceSink(delivered);
        using var logger = CreateLogger(
            monitors,
            new CollectingSink(),
            highPrioritySink,
            generalBufferSize: 4,
            highPriorityBufferSize: 4);

        logger.Error("discard first high priority event");
        Assert.IsTrue(highPrioritySink.WaitUntilAttempted());
        logger.Error("deliver second high priority event");

        Assert.IsTrue(delivered.WaitForCount(1));
        Assert.AreEqual(
            "deliver second high priority event",
            delivered.Events.Single().RenderMessage());
        Assert.AreEqual(1, monitors.HighPriority.Snapshot.DroppedMessagesCount);
    }

    [TestMethod]
    public void Metrics_use_only_bounded_channel_tags()
    {
        using var monitors = new FullNetLoggingMonitors();
        var channels = new ConcurrentBag<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == FullNetAsyncLogMonitor.MeterName)
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (_, _, tags, _) =>
            {
                var channel = tags.ToArray()
                    .Single(tag => tag.Key == "channel")
                    .Value?.ToString();
                if (channel is not null)
                {
                    channels.Add(channel);
                }
            });
        monitors.General.StartMonitoring(new FakeInspector(2, 10, 1));
        monitors.HighPriority.StartMonitoring(new FakeInspector(1, 5, 0));

        listener.Start();
        listener.RecordObservableInstruments();

        CollectionAssert.AreEquivalent(
            new[] { "general", "high_priority" },
            channels.Distinct(StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public async Task Health_degrades_only_for_high_priority_near_capacity()
    {
        using var monitors = new FullNetLoggingMonitors();
        var healthCheck = new HighPriorityLoggingHealthCheck(monitors);
        monitors.General.StartMonitoring(new FakeInspector(10, 10, 5));
        monitors.HighPriority.StartMonitoring(new FakeInspector(1, 10, 0));

        var healthy = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Healthy, healthy.Status);

        monitors.HighPriority.StartMonitoring(new FakeInspector(9, 10, 0));
        var degraded = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Degraded, degraded.Status);
    }

    [TestMethod]
    public void Service_defaults_reject_non_positive_high_priority_capacity()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"{LoggingOptions.SectionName}:HighPriorityAsyncBufferSize"] = "0",
            });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => builder.AddFullNetServiceDefaults());
    }

    [TestMethod]
    public void Service_defaults_register_dual_logging_monitors_and_ready_check()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddFullNetServiceDefaults();
        using var host = builder.Build();

        Assert.IsNotNull(host.Services.GetService<FullNetLoggingMonitors>());
        var healthOptions = host.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value;
        var registration = healthOptions.Registrations.Single(
            candidate => candidate.Name == "high_priority_logging");
        CollectionAssert.Contains(registration.Tags.ToArray(), "ready");
    }

    [TestMethod]
    public void Logger_disposal_uses_one_total_timeout_for_both_blocked_channels()
    {
        using var monitors = new FullNetLoggingMonitors();
        using var generalSink = new BlockingSink();
        using var highPrioritySink = new BlockingSink();
        var logger = CreateLogger(
            monitors,
            generalSink,
            highPrioritySink,
            generalBufferSize: 4,
            highPriorityBufferSize: 4,
            shutdownFlushTimeout: TimeSpan.FromMilliseconds(100));
        logger.Information("block general channel during shutdown");
        logger.Error("block high priority channel during shutdown");
        Assert.IsTrue(generalSink.WaitUntilEntered());
        Assert.IsTrue(highPrioritySink.WaitUntilEntered());

        var disposeTask = Task.Run(logger.Dispose);
        var completedWithinBudget = disposeTask.Wait(TimeSpan.FromSeconds(1));
        generalSink.Release();
        highPrioritySink.Release();
        var completedAfterRelease = disposeTask.Wait(TimeSpan.FromSeconds(2));

        Assert.IsTrue(completedAfterRelease);
        Assert.IsTrue(
            completedWithinBudget,
            "Logger disposal exceeded the shared shutdown flush budget.");
    }

    [TestMethod]
    public void Logger_disposal_drains_both_channels_before_timeout()
    {
        using var monitors = new FullNetLoggingMonitors();
        var generalSink = new CollectingSink();
        var highPrioritySink = new CollectingSink();
        var logger = CreateLogger(
            monitors,
            generalSink,
            highPrioritySink,
            generalBufferSize: 4,
            highPriorityBufferSize: 4,
            shutdownFlushTimeout: TimeSpan.FromSeconds(1));
        logger.Information("drain general channel");
        logger.Error("drain high priority channel");

        logger.Dispose();

        Assert.AreEqual(
            "drain general channel",
            generalSink.Events.Single().RenderMessage());
        Assert.AreEqual(
            "drain high priority channel",
            highPrioritySink.Events.Single().RenderMessage());
    }

    [TestMethod]
    public void Service_defaults_reject_out_of_range_shutdown_flush_timeout()
    {
        foreach (var invalidValue in new[] { "00:00:00", "00:00:31" })
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"{LoggingOptions.SectionName}:ShutdownFlushTimeout"] = invalidValue,
                });

            var exception = Assert.ThrowsExactly<OptionsValidationException>(
                () => builder.AddFullNetServiceDefaults());
            StringAssert.Contains(
                exception.Message,
                nameof(LoggingOptions.ShutdownFlushTimeout));
        }
    }

    private static Serilog.Core.Logger CreateLogger(
        FullNetLoggingMonitors monitors,
        ILogEventSink generalSink,
        ILogEventSink highPrioritySink,
        int generalBufferSize,
        int highPriorityBufferSize,
        TimeSpan? shutdownFlushTimeout = null)
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Verbose();
        FullNetLoggingPipeline.Configure(
            configuration,
            "Full.NET.UnitTests",
            new LoggingOptions
            {
                AsyncBufferSize = generalBufferSize,
                HighPriorityAsyncBufferSize = highPriorityBufferSize,
                ShutdownFlushTimeout =
                    shutdownFlushTimeout ?? TimeSpan.FromSeconds(5),
            },
            monitors,
            sink => sink.Sink(generalSink),
            sink => sink.Sink(highPrioritySink));
        return configuration.CreateLogger();
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly ManualResetEventSlim _received = new();

        public ConcurrentBag<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
            _received.Set();
        }

        public bool WaitForCount(int count) =>
            SpinWait.SpinUntil(
                () => Events.Count >= count,
                TimeSpan.FromSeconds(2));
    }

    private sealed class BlockingSink : ILogEventSink, IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();

        public void Emit(LogEvent logEvent)
        {
            _entered.Set();
            _release.Wait(TimeSpan.FromSeconds(10));
        }

        public bool WaitUntilEntered() => _entered.Wait(TimeSpan.FromSeconds(2));

        public void Release() => _release.Set();

        public void Dispose()
        {
            _release.Set();
            _entered.Dispose();
            _release.Dispose();
        }
    }

    private sealed class ThrowOnceSink(ILogEventSink next) :
        ILogEventSink,
        IDisposable
    {
        private readonly ManualResetEventSlim _attempted = new();
        private int _attempts;

        public void Emit(LogEvent logEvent)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                _attempted.Set();
                throw new InvalidOperationException("simulated sink failure");
            }

            next.Emit(logEvent);
        }

        public bool WaitUntilAttempted() =>
            _attempted.Wait(TimeSpan.FromSeconds(2));

        public void Dispose() => _attempted.Dispose();
    }

    private sealed record FakeInspector(
        int Count,
        int BufferSize,
        long DroppedMessagesCount) : IAsyncLogEventSinkInspector;
}
