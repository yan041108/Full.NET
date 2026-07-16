using Full.NET.Hosting.Observability;
using Serilog.Sinks.Async;

namespace Full.NET.UnitTests.Observability;

[TestClass]
public sealed class FullNetAsyncLogMonitorTests
{
    [TestMethod]
    public void Snapshot_tracks_active_inspector_and_clears_on_stop()
    {
        using var monitor = new FullNetAsyncLogMonitor();
        var inspector = new FakeInspector(25, 100, 3);

        monitor.StartMonitoring(inspector);

        Assert.AreEqual(new AsyncLogBufferSnapshot(25, 100, 3), monitor.Snapshot);

        monitor.StopMonitoring(inspector);

        Assert.AreEqual(default, monitor.Snapshot);
    }

    private sealed record FakeInspector(
        int Count,
        int BufferSize,
        long DroppedMessagesCount) : IAsyncLogEventSinkInspector;
}
