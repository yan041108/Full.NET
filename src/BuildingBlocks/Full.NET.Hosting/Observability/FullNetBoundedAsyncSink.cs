using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.Async;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 通过条数和字节双重有界的快照队列在后台线程调用单个日志 Sink。
/// </summary>
internal sealed class FullNetBoundedAsyncSink :
    ILogEventSink,
    IAsyncLogEventSinkInspector,
    ILogByteBudgetInspector
{
    private readonly BlockingCollection<LogEnvelope> _queue;
    private readonly ILogEventSink _sink;
    private readonly Action<LogEnvelope>? _emitSnapshot;
    private readonly Action<HostLogSnapshot>? _emitExternalSnapshot;
    private readonly bool _highPriority;
    private readonly bool _emitLegacySink;
    private readonly FullNetAsyncLogMonitor _monitor;
    private readonly LogQueueByteBudget _byteBudget;
    private readonly int _maxEventBytes;
    private readonly int _maxAdmissionCharge;
    private readonly Thread _worker;
    private long _droppedMessagesCount;
    private long _oversizeCount;
    private long _byteBudgetDroppedCount;
    private int _completionStarted;
    private int _monitorStopped;

    public FullNetBoundedAsyncSink(
        ILogEventSink sink,
        int bufferSize,
        string workerName,
        FullNetAsyncLogMonitor monitor,
        int maxEventBytes,
        long queueMaxBytes,
        Action<LogEnvelope>? emitSnapshot = null,
        bool emitLegacySink = true,
        Action<HostLogSnapshot>? emitExternalSnapshot = null,
        bool highPriority = false)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerName);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEventBytes);
        var maxAdmissionCharge = LogEnvelope.MaxChargeBytes(maxEventBytes, emitLegacySink);
        ArgumentOutOfRangeException.ThrowIfLessThan(queueMaxBytes, maxAdmissionCharge);

        _sink = sink;
        _emitSnapshot = emitSnapshot;
        _emitExternalSnapshot = emitExternalSnapshot;
        _highPriority = highPriority;
        _emitLegacySink = emitLegacySink;
        _monitor = monitor;
        _maxEventBytes = maxEventBytes;
        _maxAdmissionCharge = maxAdmissionCharge;
        _byteBudget = new LogQueueByteBudget(queueMaxBytes);
        _queue = new BlockingCollection<LogEnvelope>(
            new ConcurrentQueue<LogEnvelope>(),
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

    public long ReservedBytes => _byteBudget.ReservedBytes;

    public long ByteCapacity => _byteBudget.CapacityBytes;

    public long OversizeCount => Interlocked.Read(ref _oversizeCount);

    public long ByteBudgetDroppedCount => Interlocked.Read(ref _byteBudgetDroppedCount);

    public void Emit(LogEvent logEvent)
        => _ = EmitCore(logEvent, null);

    internal bool TryEmitTrustedHttp(LogEvent logEvent, HttpOperationLogRecord record)
        => EmitCore(logEvent, record);

    private bool EmitCore(LogEvent logEvent, HttpOperationLogRecord? trustedHttp)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (Volatile.Read(ref _completionStarted) != 0)
        {
            Interlocked.Increment(ref _droppedMessagesCount);
            return false;
        }

        // 先按最大封套预留，避免大量并发调用在队列已满时仍构造临时事件图。
        if (!_byteBudget.TryReserve(_maxAdmissionCharge))
        {
            Interlocked.Increment(ref _byteBudgetDroppedCount);
            Interlocked.Increment(ref _droppedMessagesCount);
            return false;
        }

        var reservedCharge = _maxAdmissionCharge;
        var queued = false;
        try
        {
            LogEnvelope? envelope;
            try
            {
                if (!LogEnvelopeBuilder.TryBuild(
                        logEvent,
                        _maxEventBytes,
                        out envelope,
                        retainLegacyEvent: _emitLegacySink,
                        trustedHttp: trustedHttp))
                {
                    Interlocked.Increment(ref _oversizeCount);
                    Interlocked.Increment(ref _droppedMessagesCount);
                    return false;
                }
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _droppedMessagesCount);
                SelfLog.WriteLine(
                    "Full.NET log snapshot rejected one event with {0}.",
                    exception.GetType().FullName);
                return false;
            }

            // 快照完成后只保留实际 UTF-8 字节和封套费用。
            var unusedCharge = _maxAdmissionCharge - envelope!.ChargeBytes;
            if (unusedCharge > 0)
            {
                _byteBudget.Release(unusedCharge);
            }
            reservedCharge = envelope.ChargeBytes;

            try
            {
                if (Volatile.Read(ref _completionStarted) != 0
                    || !_queue.TryAdd(envelope))
                {
                    Interlocked.Increment(ref _droppedMessagesCount);
                }
                else
                {
                    queued = true;
                }
            }
            catch (InvalidOperationException)
            {
                // CompleteAdding 与 TryAdd 的竞态只表示退出已经开始，调用方仍必须保持非阻塞。
                Interlocked.Increment(ref _droppedMessagesCount);
            }
        }
        finally
        {
            if (!queued)
            {
                _byteBudget.Release(reservedCharge);
            }
        }

        return queued;
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
        while (_queue.TryTake(out var envelope))
        {
            _byteBudget.Release(envelope.ChargeBytes);
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
            foreach (var envelope in _queue.GetConsumingEnumerable())
            {
                try
                {
                    var failed = false;
                    if (_emitSnapshot is not null)
                    {
                        try
                        {
                            _emitSnapshot(envelope);
                        }
                        catch (Exception exception)
                        {
                            failed = true;
                            ReportSinkFailure(exception);
                        }
                    }

                    if (_emitExternalSnapshot is not null)
                    {
                        try
                        {
                            // 只把经过快照安全投影且 ID 与 JSON 一致的数据交给外部适配器。
                            _emitExternalSnapshot(new HostLogSnapshot(
                                envelope.Utf8Json,
                                envelope.LogEventId ?? throw new InvalidOperationException(
                                    "The external log snapshot requires a validated event ID."),
                                _highPriority));
                        }
                        catch (Exception exception)
                        {
                            failed = true;
                            ReportSinkFailure(exception);
                        }
                    }

                    if (_emitLegacySink)
                    {
                        try
                        {
                            _sink.Emit(envelope.LegacyEvent
                                ?? throw new InvalidOperationException(
                                    "The legacy logging sink requires a safe event copy."));
                        }
                        catch (Exception exception)
                        {
                            failed = true;
                            ReportSinkFailure(exception);
                        }
                    }

                    if (failed)
                    {
                        Interlocked.Increment(ref _droppedMessagesCount);
                    }
                }
                finally
                {
                    // 只有下游调用真正结束后才能归还在途缓冲预算。
                    _byteBudget.Release(envelope.ChargeBytes);
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
                try
                {
                    disposable.Dispose();
                }
                catch (Exception exception)
                {
                    // 日志出口释放失败不能从后台线程逸出并终止宿主进程。
                    SelfLog.WriteLine(
                        "Full.NET asynchronous logging sink failed to dispose with {0}.",
                        exception.GetType().FullName);
                }
            }
        }
    }

    private static void ReportSinkFailure(Exception exception) =>
        SelfLog.WriteLine(
            "Full.NET asynchronous logging sink rejected one event with {0}; "
            + "the worker will continue.",
            exception.GetType().FullName);
}
