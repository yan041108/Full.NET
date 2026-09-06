using System.Collections.Concurrent;

namespace Full.NET.Modules.Mqtt.Security;

/// <summary>按用户维度的 MQTT 发布速率限制器，采用滑动一分钟窗口。</summary>
internal sealed class MqttPublishRateLimiter
{
    private readonly ConcurrentDictionary<Guid, RateWindow> _windows = new();

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

        var window = _windows.GetOrAdd(userId, _ => new RateWindow());
        return window.TryAcquire(maximumPerMinute, nowUtc);
    }

    private sealed class RateWindow
    {
        private readonly object _sync = new();
        private readonly Queue<DateTimeOffset> _timestamps = new();

        public bool TryAcquire(int maximumPerMinute, DateTimeOffset nowUtc)
        {
            lock (_sync)
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
                return true;
            }
        }
    }
}
