using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace Full.NET.Caching.Fusion.Health;

/// <summary>透明观察已注册 SDK Backplane 的结果，不创建连接、探针消息或独立订阅循环。</summary>
internal sealed class ObservedFusionCacheBackplane(
    IFusionCacheBackplane inner, CacheBackplaneHealthState state) : IFusionCacheBackplane, IDisposable
{
    private readonly object _lifecycleGate = new();
    private long _session = state.BeginSession();
    private int _disposed;

    public void Subscribe(BackplaneSubscriptionOptions options)
    {
        var session = SubscriptionSession();
        var sequence = state.BeginOperation();
        try
        {
            inner.Subscribe(Observe(options, session));
            state.Complete(session, sequence, subscription: true, success: true);
        }
        catch
        {
            state.Complete(session, sequence, subscription: true, success: false);
            throw;
        }
    }

    public async ValueTask SubscribeAsync(BackplaneSubscriptionOptions options)
    {
        var session = SubscriptionSession();
        var sequence = state.BeginOperation();
        try
        {
            await inner.SubscribeAsync(Observe(options, session)).ConfigureAwait(false);
            state.Complete(session, sequence, subscription: true, success: true);
        }
        catch
        {
            state.Complete(session, sequence, subscription: true, success: false);
            throw;
        }
    }

    public void Publish(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default)
    {
        var session = Volatile.Read(ref _session);
        var sequence = state.BeginOperation();
        try
        {
            inner.Publish(message, options, token);
            state.Complete(session, sequence, subscription: false, success: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch
        {
            state.Complete(session, sequence, subscription: false, success: false);
            throw;
        }
    }

    public async ValueTask PublishAsync(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default)
    {
        var session = Volatile.Read(ref _session);
        var sequence = state.BeginOperation();
        try
        {
            await inner.PublishAsync(message, options, token).ConfigureAwait(false);
            state.Complete(session, sequence, subscription: false, success: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch
        {
            state.Complete(session, sequence, subscription: false, success: false);
            throw;
        }
    }

    private BackplaneSubscriptionOptions Observe(BackplaneSubscriptionOptions options, long session) => new(
        options.CacheName, options.CacheInstanceId, options.ChannelName,
        info => { OnConnection(info, session); options.ConnectHandler?.Invoke(info); },
        message => { OnMessage(message, session); options.IncomingMessageHandler?.Invoke(message); },
        async info =>
        {
            OnConnection(info, session);
            if (options.ConnectHandlerAsync is { } handler) await handler(info).ConfigureAwait(false);
        },
        async message =>
        {
            OnMessage(message, session);
            if (options.IncomingMessageHandlerAsync is { } handler) await handler(message).ConfigureAwait(false);
        });

    private void OnConnection(BackplaneConnectionInfo info, long session)
    {
        if (info.IsReconnection) state.Reconnected(session);
    }

    private void OnMessage(BackplaneMessage message, long session)
    {
        // SDK 的消息事件可能先于有效性校验；无效消息不能证明订阅已经可用。
        if (message.IsValid()) state.ObserveSubscription(session);
    }

    private long SubscriptionSession()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            // Unsubscribe 不是 Dispose；SDK 可以复用同一实例，回调必须捕获各自周期的代际。
            if (_session == 0) _session = state.BeginSession();
            return _session;
        }
    }

    private void EndSubscriptionSession()
    {
        lock (_lifecycleGate)
        {
            state.EndSession(_session);
            _session = 0;
        }
    }

    public void Unsubscribe()
    {
        EndSubscriptionSession();
        inner.Unsubscribe();
    }

    public ValueTask UnsubscribeAsync()
    {
        EndSubscriptionSession();
        return inner.UnsubscribeAsync();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        EndSubscriptionSession();
        // 底层 Backplane 由原注册容器管理；包装器不重复取得或释放其连接所有权。
    }
}
