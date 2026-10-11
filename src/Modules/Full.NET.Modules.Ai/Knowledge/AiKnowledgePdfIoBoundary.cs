using System.Collections.Concurrent;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>仅隔离 PDF 外部端口的同步阻塞；普通操作与清理各自拥有有限资源。</summary>
internal static class AiKnowledgePdfIoBoundary
{
    private static readonly AiKnowledgePdfIoScheduler Operations = new(4, 8);
    private static readonly AiKnowledgePdfIoScheduler Cleanup = new(4, 8);

    internal static Task<T> RunAsync<T>(Func<Task<T>> action) => Operations.RunAsync(action);
    internal static AiKnowledgePdfIoScheduler.Reservation ReserveCleanup() => Cleanup.Reserve();
    internal static Task CleanupAsync(Func<Task> action) => Cleanup.RunAsync(async () =>
    {
        await action().ConfigureAwait(false);
        return true;
    });
}

/// <summary>专用后台线程承接同步入口，异步等待仍由 await 完成；未完成操作始终占用准入容量。</summary>
internal sealed class AiKnowledgePdfIoScheduler : IAsyncDisposable
{
    /// <summary>取得流或收费前保留清理容量；迟到清理可持有额外引用，且只提交一次。</summary>
    internal sealed class Reservation(AiKnowledgePdfIoScheduler owner) : IDisposable
    {
        internal int References = 1;
        internal bool Consumed;
        private int _ownerDisposed;

        internal Task RunAsync(Func<Task> action) => owner.RunReservedAsync(this, action);

        internal IDisposable Retain()
        {
            lock (owner._gate)
            {
                if (References == 0 || Consumed) throw new InvalidOperationException("fn_ai_pdf_cleanup_reservation_closed");
                References++;
                return new Hold(this);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ownerDisposed, 1) == 0) Release();
        }

        private void Release()
        {
            lock (owner._gate)
            {
                References--;
                if (References == 0 && !Consumed) { owner._active--; owner.CompleteDrain(); }
            }
        }

        private sealed class Hold(Reservation reservation) : IDisposable
        {
            private int _disposed;
            public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) reservation.Release(); }
        }
    }
    private readonly object _gate = new();
    private readonly BlockingCollection<IWork> _queue;
    private readonly int _capacity;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _accepting = true;
    private int _active;
    private int _workersRemaining;
    private int _disposed;

    internal AiKnowledgePdfIoScheduler(int workers, int queueCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(workers, 4);
        ArgumentOutOfRangeException.ThrowIfNegative(queueCapacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(queueCapacity, 8);
        _capacity = workers + queueCapacity;
        // 队列也容纳尚未被启动线程领取的操作；总准入上限由 _active 独立限制。
        _queue = new(_capacity);
        _workersRemaining = workers;
        var started = 0;
        try
        {
            for (; started < workers; started++)
            {
                var thread = new Thread(Consume) { IsBackground = true, Name = "Full.NET PDF IO" };
                // 线程基线不能保留首次请求；每项工作单独恢复捕获的上下文。
                if (ExecutionContext.IsFlowSuppressed()) thread.Start();
                else { using (ExecutionContext.SuppressFlow()) thread.Start(); }
            }
        }
        catch
        {
            lock (_gate)
            {
                _workersRemaining -= workers - started;
                _accepting = false;
                _queue.CompleteAdding();
                CompleteDrain();
            }
            _ = DisposeAsync();
            throw;
        }
    }

    internal Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            // 以失败任务进入调用方原有 finally，确保它仍释放 IO 令牌并恢复证明上下文。
            if (!_accepting) return Task.FromException<T>(new ObjectDisposedException(nameof(AiKnowledgePdfIoScheduler)));
            if (_active == _capacity) return Task.FromException<T>(new IOException("fn_ai_pdf_io_capacity_exhausted"));
            var work = new Work<T>(this, action, ExecutionContext.Capture());
            _active++;
            if (!_queue.TryAdd(work))
            {
                _active--;
                return Task.FromException<T>(new IOException("fn_ai_pdf_io_capacity_exhausted"));
            }
            return work.Completion.Task;
        }
    }

    internal Reservation Reserve()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(!_accepting, this);
            if (_active == _capacity) throw new IOException("fn_ai_pdf_io_capacity_exhausted");
            _active++;
            return new(this);
        }
    }

    private Task RunReservedAsync(Reservation reservation, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            if (reservation.References == 0 || reservation.Consumed)
                return Task.FromException(new InvalidOperationException("fn_ai_pdf_cleanup_reservation_closed"));
            reservation.Consumed = true;
            var work = new Work<bool>(this, async () => { await action().ConfigureAwait(false); return true; }, ExecutionContext.Capture());
            // 准入已保留，关闭也不能拒绝已接纳资源的最终清理。
            _queue.Add(work);
            return work.Completion.Task;
        }
    }

    private void Consume()
    {
        try { while (ConsumeNext()) { } }
        finally { lock (_gate) { _workersRemaining--; CompleteDrain(); } }
    }

    private bool ConsumeNext()
    {
        // 仅专用线程阻塞领取；单独栈帧避免空闲线程保留上一项请求上下文。
        if (!_queue.TryTake(out var work, Timeout.Infinite)) return false;
        work.Start();
        return true;
    }

    private void Complete(Action complete)
    {
        lock (_gate)
        {
            _active--;
            // 异步续体保证调用者不能在锁内重入；观察到完成时容量已经可复用。
            complete();
            CompleteDrain();
        }
    }

    private void CompleteDrain()
    {
        if (!_accepting && _active == 0)
        {
            // 未消费的保留额度也属于已接纳工作，释放前保持线程可接收其清理。
            if (!_queue.IsAddingCompleted) _queue.CompleteAdding();
            if (_workersRemaining == 0) _drained.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_accepting) { _accepting = false; CompleteDrain(); }
        }
        // 不强制终止任意端口；关闭等待已接纳工作与线程退出，不提前处置队列。
        await _drained.Task.ConfigureAwait(false);
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _queue.Dispose();
    }

    private interface IWork { void Start(); }

    private sealed class Work<T>(AiKnowledgePdfIoScheduler owner, Func<Task<T>> action, ExecutionContext? context) : IWork
    {
        internal TaskCompletionSource<T> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Start()
        {
            if (context is null) _ = ExecuteAsync();
            else ExecutionContext.Run(context, static state => { _ = ((Work<T>)state!).ExecuteAsync(); }, this);
        }

        private async Task ExecuteAsync()
        {
            try
            {
                var result = await action().ConfigureAwait(false);
                owner.Complete(() => Completion.TrySetResult(result));
            }
            catch (OperationCanceledException error) { owner.Complete(() => Completion.TrySetCanceled(error.CancellationToken)); }
            catch (Exception error) { owner.Complete(() => Completion.TrySetException(error)); }
        }
    }
}
