using System.Diagnostics.Metrics;
using Serilog.Sinks.Async;

namespace Full.NET.Hosting.Observability;

public readonly record struct AsyncLogBufferSnapshot(
    int Count,
    int BufferSize,
    long DroppedMessagesCount);

public sealed class FullNetAsyncLogMonitor : IAsyncLogEventSinkMonitor, IDisposable
{
    private readonly Meter _meter = new("Full.NET.Logging");
    private IAsyncLogEventSinkInspector? _inspector;

    public FullNetAsyncLogMonitor()
    {
        _meter.CreateObservableGauge(
            "fullnet.logging.queue.depth",
            () => Snapshot.Count);
        _meter.CreateObservableGauge(
            "fullnet.logging.queue.capacity",
            () => Snapshot.BufferSize);
        _meter.CreateObservableGauge(
            "fullnet.logging.events.dropped",
            () => Snapshot.DroppedMessagesCount);
    }

    public AsyncLogBufferSnapshot Snapshot
    {
        get
        {
            var inspector = Volatile.Read(ref _inspector);
            return inspector is null
                ? default
                : new AsyncLogBufferSnapshot(
                    inspector.Count,
                    inspector.BufferSize,
                    inspector.DroppedMessagesCount);
        }
    }

    public void StartMonitoring(IAsyncLogEventSinkInspector inspector) =>
        Volatile.Write(ref _inspector, inspector);

    public void StopMonitoring(IAsyncLogEventSinkInspector inspector) =>
        Interlocked.CompareExchange(ref _inspector, null, inspector);

    public void Dispose()
    {
        Volatile.Write(ref _inspector, null);
        _meter.Dispose();
    }
}
