using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.Async;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 通过固定容量队列在后台线程调用单个日志 Sink。
/// </summary>
internal sealed class FullNetBoundedAsyncSink :
    ILogEventSink,
    IAsyncLogEventSinkInspector
{
    private readonly BlockingCollection<LogEvent> _queue;
    private readonly ILogEventSink _sink;
    private readonly FullNetAsyncLogMonitor _monitor;
    private readonly Thread _worker;
    private long _droppedMessagesCount;
    private int _completionStarted;
    private int _monitorStopped;

    public FullNetBoundedAsyncSink(
        ILogEventSink sink,
        int bufferSize,
        string workerName,
        FullNetAsyncLogMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerName);
        ArgumentNullException.ThrowIfNull(monitor);

        _sink = sink;
        _monitor = monitor;
        _queue = new BlockingCollection<LogEvent>(
            new ConcurrentQueue<LogEvent>(),
            bufferSize);
        _worker = new Thread(Consume)
        {
            IsBackground = true,
            Name = workerName,
        };

        _monitor.StartMonitoring(this);
        _worker.Start();
    }

    public int BufferSize => _queue.BoundedCapacity;

    public int Count => _queue.Count;

    public long DroppedMessagesCount =>
        Interlocked.Read(ref _droppedMessagesCount);

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        try
        {
            if (Volatile.Read(ref _completionStarted) != 0
                || !_queue.TryAdd(logEvent))
            {
                Interlocked.Increment(ref _droppedMessagesCount);
            }
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding 与 TryAdd 的竞态只表示退出已经开始，调用方仍必须保持非阻塞。
            Interlocked.Increment(ref _droppedMessagesCount);
        }
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _completionStarted, 1) == 0)
        {
            _queue.CompleteAdding();
        }
    }

    public bool WaitForCompletion(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return _worker.Join(TimeSpan.Zero);
        }

        return _worker.Join(timeout);
    }

    public void AbandonPending()
    {
        while (_queue.TryTake(out _))
        {
            Interlocked.Increment(ref _droppedMessagesCount);
        }

        if (Interlocked.Exchange(ref _monitorStopped, 1) == 0)
        {
            _monitor.StopMonitoring(this);
        }
    }

    private void Consume()
    {
        try
        {
            foreach (var logEvent in _queue.GetConsumingEnumerable())
            {
                try
                {
                    _sink.Emit(logEvent);
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref _droppedMessagesCount);
                    SelfLog.WriteLine(
                        "Full.NET asynchronous logging sink rejected one event with {0}; "
                        + "the worker will continue.",
                        exception.GetType().FullName);
                }
            }
        }
        catch (Exception exception)
        {
            SelfLog.WriteLine(
                "Full.NET asynchronous logging worker stopped unexpectedly with {0}.",
                exception.GetType().FullName);
        }
        finally
        {
            if (_sink is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
