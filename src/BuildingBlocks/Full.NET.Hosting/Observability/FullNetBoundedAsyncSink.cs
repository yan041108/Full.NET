using System.Collections.Concurrent;
using System.Diagnostics;
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
    ILogByteBudgetInspector,
    ILogProgressInspector
{
    private readonly BlockingCollection<LogEnvelope> _queue;
    private readonly ConcurrentQueue<LogEnvelope> _pending = new();
    private readonly Action<LogEnvelope>? _emitChannelSnapshot;
    private readonly Action<LogEnvelope>? _emitSnapshot;
    private readonly Func<HostLogSnapshot, bool>? _emitExternalSnapshot;
    private readonly bool _highPriority;
    private readonly FullNetAsyncLogMonitor _monitor;
    private readonly LogQueueByteBudget _byteBudget;
    private readonly int _maxEventBytes;
    private readonly int _maxAdmissionCharge;
    private readonly LogIndexRoutingPolicy? _routingPolicy;
    private readonly Thread _worker;
    private readonly LocalFileLogWriter? _localFile;
    private long _droppedMessagesCount;
    private long _oversizeCount;
    private long _byteBudgetDroppedCount;
    private int _completionStarted;
    private int _admittedCount;
    private long _inFlightTimestamp;

    public FullNetBoundedAsyncSink(
        Action<LogEnvelope>? emitChannelSnapshot,
        int bufferSize,
        string workerName,
        FullNetAsyncLogMonitor monitor,
        int maxEventBytes,
        long queueMaxBytes,
        Action<LogEnvelope>? emitSnapshot = null,
        Func<HostLogSnapshot, bool>? emitExternalSnapshot = null,
        bool highPriority = false,
        LogIndexRoutingPolicy? routingPolicy = null,
        LocalFileLogWriter? localFile = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerName);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEventBytes);
        var maxAdmissionCharge = LogEnvelope.MaxChargeBytes(maxEventBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(queueMaxBytes, maxAdmissionCharge);

        _emitChannelSnapshot = emitChannelSnapshot;
        _emitSnapshot = emitSnapshot;
        _emitExternalSnapshot = emitExternalSnapshot;
        _highPriority = highPriority;
        _monitor = monitor;
        _maxEventBytes = maxEventBytes;
        _maxAdmissionCharge = maxAdmissionCharge;
        _routingPolicy = routingPolicy;
        _localFile = localFile;
        _byteBudget = new LogQueueByteBudget(queueMaxBytes);
        _queue = new BlockingCollection<LogEnvelope>(
            _pending,
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

    public double OldestPendingSeconds
    {
        get
        {
            var oldest = Interlocked.Read(ref _inFlightTimestamp);
            if (_pending.TryPeek(out var next)) oldest = oldest == 0 ? next.QueuedAtTimestamp : Math.Min(oldest, next.QueuedAtTimestamp);
            return oldest == 0 ? 0 : Stopwatch.GetElapsedTime(oldest).TotalSeconds;
        }
    }

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
        _monitor.RecordOffered(logEvent.Level);

        if (Volatile.Read(ref _completionStarted) != 0)
        {
            Interlocked.Increment(ref _droppedMessagesCount);
            return false;
        }

        // 名额覆盖正在构建及等待消费的事件，避免条数已满但字节有余量时继续构建快照。
        if (!TryReserveSlot())
        {
            Interlocked.Increment(ref _droppedMessagesCount);
            return false;
        }

        // 在构建前同时满足条数和最大封套字节预算；任一拒绝都不等待下游。
        if (!_byteBudget.TryReserve(_maxAdmissionCharge))
        {
            Interlocked.Decrement(ref _admittedCount);
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
                        trustedHttp: trustedHttp,
                        routingPolicy: _routingPolicy))
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
                Interlocked.Decrement(ref _admittedCount);
            }
        }

        return queued;
    }

    private bool TryReserveSlot()
    {
        while (true)
        {
            var current = Volatile.Read(ref _admittedCount);
            if (current >= BufferSize) return false;
            if (Interlocked.CompareExchange(ref _admittedCount, current + 1, current) == current)
                return true;
        }
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _completionStarted, 1) == 0)
        {
            _queue.CompleteAdding();
            _monitor.WorkerStopping();
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
            Interlocked.Decrement(ref _admittedCount);
            _byteBudget.Release(envelope.ChargeBytes);
            Interlocked.Increment(ref _droppedMessagesCount);
        }

        // 当前下游可能仍在途；保留监控直到真实消费线程结束。
    }

    private void Consume()
    {
        try
        {
            while (!_queue.IsCompleted)
            {
                // 文件刷新在原消费线程执行；不占用请求线程，也不为刷新新增无界任务。
                if (!_queue.TryTake(out var envelope, _localFile is null ? Timeout.Infinite : 100))
                {
                    _localFile?.FlushIfDue();
                    continue;
                }
                // 出队即释放等待名额；字节预算仍保留至下游真正结束。
                Interlocked.Decrement(ref _admittedCount);
                try
                {
                    Interlocked.Exchange(ref _inFlightTimestamp, envelope.QueuedAtTimestamp);
                    var failed = false;
                    if (_localFile is not null)
                    {
                        try { _localFile.Emit(envelope); _localFile.FlushIfDue(); }
                        // 文件出口已自行限频报告；这里仅累计当前事件丢弃，避免故障日志洪峰。
                        catch (Exception) { failed = true; }
                    }
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
                            if (!_emitExternalSnapshot(new HostLogSnapshot(
                                envelope.Utf8Json,
                                envelope.LogEventId ?? throw new InvalidOperationException(
                                    "The external log snapshot requires a validated event ID."),
                                _highPriority))) failed = true;
                        }
                        catch (Exception exception)
                        {
                            failed = true;
                            ReportSinkFailure(exception);
                        }
                    }

                    if (_emitChannelSnapshot is not null)
                    {
                        try
                        {
                            _emitChannelSnapshot(envelope);
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
                    _monitor.RecordConsumed(failed);
                }
                finally
                {
                    // 只有下游调用真正结束后才能归还在途缓冲预算。
                    _byteBudget.Release(envelope.ChargeBytes);
                    Interlocked.Exchange(ref _inFlightTimestamp, 0);
                }
            }
        }
        catch (Exception exception)
        {
            _monitor.WorkerFailed();
            Complete();
            AbandonPending();
            LoggingInternalDiagnostics.Report("worker_stopped", exception);
            SelfLog.WriteLine(
                "Full.NET asynchronous logging worker stopped unexpectedly with {0}.",
                exception.GetType().FullName);
        }
        finally
        {
            // 流最终关闭留在后台线程，磁盘阻塞不得突破宿主共享退出等待预算。
            try { _localFile?.Dispose(); }
            catch (Exception exception)
            {
                _monitor.WorkerFailed();
                LoggingInternalDiagnostics.Report("local_file_dispose", exception);
            }
            _monitor.StopMonitoring(this);
        }
    }

    private static void ReportSinkFailure(Exception exception) =>
        LoggingInternalDiagnostics.Report("sink_rejected", exception);
}
