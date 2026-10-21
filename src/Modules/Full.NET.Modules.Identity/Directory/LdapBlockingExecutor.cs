using System.Collections.Concurrent;

namespace Full.NET.Modules.Identity.Directory;

/// <summary>系统 LDAP Bind 只有同步接口，固定专用线程和有界队列隔离阻塞，不能占用请求线程或无界 Task.Run。</summary>
internal sealed class LdapBlockingExecutor : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new(64);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Thread[] _threads;

    internal LdapBlockingExecutor()
    {
        _threads = Enumerable.Range(0, 4).Select(index => new Thread(() =>
        {
            foreach (var work in _queue.GetConsumingEnumerable()) work();
        }) { IsBackground = true, Name = $"Full.NET LDAP {index}" }).ToArray();
        foreach (var thread in _threads) thread.Start();
    }

    internal async Task<T> InvokeAsync<T>(Func<CancellationToken, T> operation, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        try
        {
            if (!_queue.TryAdd(() =>
            {
                if (token.IsCancellationRequested) return;
                try
                {
                    var result = operation(token);
                    token.ThrowIfCancellationRequested();
                    completion.TrySetResult(result);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { completion.TrySetCanceled(token); }
                catch (Exception exception) { completion.TrySetException(exception); }
            })) throw new LdapOperationCapacityException();
        }
        catch (InvalidOperationException) when (_shutdown.IsCancellationRequested)
        {
            throw new OperationCanceledException(_shutdown.Token);
        }
        return await completion.Task.ConfigureAwait(false);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _queue.CompleteAdding();
        foreach (var thread in _threads) thread.Join(TimeSpan.FromSeconds(2));
        // 专用线程可能仍在不可取消的系统调用中；队列由最后一个线程退出后随实例回收，不能抢先释放。
        _shutdown.Dispose();
    }
}

/// <summary>LDAP 有界执行队列已满，由操作服务映射为可回退的限流错误。</summary>
internal sealed class LdapOperationCapacityException : Exception { }
