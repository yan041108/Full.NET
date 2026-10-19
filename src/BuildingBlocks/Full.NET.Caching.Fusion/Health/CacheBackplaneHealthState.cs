namespace Full.NET.Caching.Fusion.Health;

/// <summary>记录实际订阅和发布证据；L2 读取或 SDK 熔断器计时闭合不能代替这些证据。</summary>
internal sealed class CacheBackplaneHealthState
{
    private readonly object _gate = new();
    private long _session;
    private long _sequence;
    private long _subscriptionSequence;
    private long _publicationSequence;
    private int _subscription;
    private int _publication;

    public CacheBackplaneStatus Status
    {
        get
        {
            lock (_gate)
                return _subscription < 0 || _publication < 0 ? CacheBackplaneStatus.Failed
                    : _subscription > 0 && _publication > 0 ? CacheBackplaneStatus.Operational
                    : CacheBackplaneStatus.Unknown;
        }
    }

    public long BeginSession()
    {
        lock (_gate)
        {
            _subscription = _publication = 0;
            return ++_session;
        }
    }

    public void EndSession(long session)
    {
        lock (_gate)
        {
            if (session != _session) return;
            // 已退役实例的回调和在途操作不再影响当前实例的健康结果。
            ++_session;
            _subscription = _publication = 0;
        }
    }

    public long BeginOperation()
    {
        lock (_gate) return ++_sequence;
    }

    public void Complete(long session, long sequence, bool subscription, bool success)
    {
        lock (_gate)
        {
            if (session != _session) return;
            ref var latest = ref (subscription ? ref _subscriptionSequence : ref _publicationSequence);
            if (sequence < latest) return;
            // 按操作开始顺序保留较新的结果，避免迟到成功覆盖后来已经观察到的失败。
            latest = sequence;
            if (subscription) _subscription = success ? 1 : -1;
            else _publication = success ? 1 : -1;
        }
    }

    public void ObserveSubscription(long session)
    {
        lock (_gate)
        {
            if (session != _session) return;
            _subscriptionSequence = ++_sequence;
            _subscription = 1;
        }
    }

    public void Reconnected(long session)
    {
        lock (_gate)
        {
            if (session != _session) return;
            // 连接建立不证明 ACL 或订阅恢复；失效旧成功证据，保留尚未恢复的失败。
            _subscriptionSequence = _publicationSequence = ++_sequence;
            if (_subscription > 0) _subscription = 0;
            if (_publication > 0) _publication = 0;
        }
    }
}

/// <summary>未知和故障分别处理：未知不逐次升级为故障，也不报告为健康。</summary>
internal enum CacheBackplaneStatus { Unknown, Operational, Failed }
