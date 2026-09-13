namespace Full.NET.Modules.Mqtt.Security;

/// <summary>按用户维度的 MQTT 发布速率限制器，采用滑动一分钟窗口。</summary>
internal sealed class MqttPublishRateLimiter
{
    // 上限约束状态内存；容量耗尽时拒绝新用户，不能淘汰活跃窗口让限流失效。
    private const int MaximumUserWindows = 10000;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, RateWindow> _windows = new();
    private DateTimeOffset _nextCleanupUtc;

    /// <summary>尝试为指定用户占用一次发布配额。</summary>
    /// <param name="userId">发布用户标识。</param>
    /// <param name="maximumPerMinute">每分钟允许的最大发布次数。</param>
    /// <param name="nowUtc">当前 UTC 时间。</param>
    /// <returns>未超限时返回 <see langword="true"/>。</returns>
    public bool TryAcquire(Guid userId, int maximumPerMinute, DateTimeOffset nowUtc)
    {
        if (maximumPerMinute <= 0)
        {
            return false;
        }

        // 回收和占用共享锁，避免调用方拿着已移除窗口继续发放配额。
        lock (_sync)
        {
            if (nowUtc >= _nextCleanupUtc)
            {
                _nextCleanupUtc = nowUtc.AddMinutes(1);
                var threshold = nowUtc.AddMinutes(-1);
                foreach (var key in _windows.Where(pair => pair.Value.LastAcceptedUtc < threshold)
                             .Select(pair => pair.Key).ToArray())
                    _windows.Remove(key);
            }
            if (!_windows.TryGetValue(userId, out var window))
            {
                if (_windows.Count >= MaximumUserWindows) return false;
                window = new RateWindow();
                _windows.Add(userId, window);
            }
            return window.TryAcquire(maximumPerMinute, nowUtc);
        }
    }

    private sealed class RateWindow
    {
        private readonly Queue<DateTimeOffset> _timestamps = new();
        public DateTimeOffset LastAcceptedUtc { get; private set; }

        public bool TryAcquire(int maximumPerMinute, DateTimeOffset nowUtc)
        {
            var threshold = nowUtc.AddMinutes(-1);
            while (_timestamps.Count > 0 && _timestamps.Peek() < threshold)
            {
                _timestamps.Dequeue();
            }

            if (_timestamps.Count >= maximumPerMinute)
            {
                return false;
            }

            _timestamps.Enqueue(nowUtc);
            LastAcceptedUtc = nowUtc;
            return true;
        }
    }
}
