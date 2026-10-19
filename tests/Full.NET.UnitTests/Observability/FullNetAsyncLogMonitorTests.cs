using System.Diagnostics.Metrics;
using Full.NET.Hosting.Observability;
using Serilog.Events;
using Serilog.Sinks.Async;

namespace Full.NET.UnitTests.Observability;

/// <summary>单元测试（可观测）：Full Net Async Log Monitor。</summary>
[TestClass]
public sealed class FullNetAsyncLogMonitorTests
{
    [TestMethod]
    public void Snapshot_preserves_terminal_drop_count_after_stop()
    {
        using var monitor = new FullNetAsyncLogMonitor();
        var inspector = new FakeInspector(25, 100, 3);

        monitor.StartMonitoring(inspector);

        Assert.AreEqual(new AsyncLogBufferSnapshot(25, 100, 3), monitor.Snapshot);

        monitor.StopMonitoring(inspector);

        Assert.AreEqual(new AsyncLogBufferSnapshot(0, 0, 3), monitor.Snapshot);
        Assert.AreEqual(3, monitor.WorkerState);
    }

    [TestMethod]
    public void Worker_failure_is_not_hidden_by_stop()
    {
        using var monitor = new FullNetAsyncLogMonitor();
        var inspector = new FakeInspector(0, 100, 0);
        monitor.StartMonitoring(inspector);
        monitor.WorkerFailed();
        monitor.StopMonitoring(inspector);
        Assert.AreEqual(4, monitor.WorkerState);
    }

    [TestMethod, DoNotParallelize]
    public void Metrics_publish_monotonic_drops_and_fixed_channel_outcome_labels()
    {
        var instruments = new Dictionary<string, Instrument>();
        var values = new Dictionary<string, long>();
        var labels = new Dictionary<string, Dictionary<string, object?>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name != FullNetAsyncLogMonitor.MeterName) return;
            instruments[instrument.Name] = instrument;
            current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            values[instrument.Name] = value;
            labels[instrument.Name] = tags.ToArray().ToDictionary(item => item.Key, item => item.Value);
        });
        listener.Start();
        using var monitor = new FullNetAsyncLogMonitor();
        var inspector = new FakeInspector(1, 10, 7);
        monitor.StartMonitoring(inspector);
        monitor.RecordOffered(LogEventLevel.Fatal);
        monitor.RecordConsumed(failed: true);
        listener.RecordObservableInstruments();
        monitor.StopMonitoring(inspector);
        listener.RecordObservableInstruments();

        Assert.IsInstanceOfType<ObservableCounter<long>>(instruments["fullnet.logging.events.dropped"]);
        Assert.AreEqual(7L, values["fullnet.logging.events.dropped"]);
        Assert.AreEqual("general", labels["fullnet.logging.events.dropped"]["channel"]);
        Assert.AreEqual("Critical", labels["fullnet.logging.events.offered"]["level"]);
        Assert.AreEqual("failed", labels["fullnet.logging.events.consumed"]["outcome"]);
        Assert.AreEqual("s", instruments["fullnet.logging.queue.oldest_age"].Unit);
        Assert.AreEqual(3L, values["fullnet.logging.worker.state"]);
    }

    private sealed record FakeInspector(
        int Count,
        int BufferSize,
        long DroppedMessagesCount) : IAsyncLogEventSinkInspector;
}
