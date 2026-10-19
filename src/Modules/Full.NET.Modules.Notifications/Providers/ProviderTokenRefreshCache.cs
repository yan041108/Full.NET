using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Time;

namespace Full.NET.Modules.Notifications.Providers;

/// <summary>Provider 私有令牌缓存：按凭据指纹合并刷新，等待者取消不取消其他调用者，宿主关闭负责排空。</summary>
internal sealed class ProviderTokenRefreshCache(IClock clock) : IAsyncDisposable
{
    private const int MaximumEntries = 1024;
    private static readonly TimeSpan RenewalSkew = TimeSpan.FromSeconds(60);
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private DateTimeOffset _nextCleanupUtc;
    private bool _disposed;

    /// <summary>当前缓存条目数；仅供测试观测，不表示有效令牌数量。</summary>
    internal int Count { get { lock (_sync) return _entries.Count; } }

    /// <summary>
    /// 按 identity+secret 指纹返回未过期令牌；并发调用合并为单次刷新，调用方取消只影响自身等待。
    /// </summary>
    public async ValueTask<string> GetAsync(string identity, string secret,
        Func<CancellationToken, ValueTask<(string Token, DateTimeOffset ExpiresAtUtc)>> factory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 长度前缀消除分隔符歧义，缓存索引不持有明文凭据。
        var bytes = Encoding.UTF8.GetBytes($"{identity.Length}:{identity}{secret}");
        string key;
        try { key = Convert.ToHexString(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }

        Task<string> refresh;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = clock.UtcNow;
            if (now >= _nextCleanupUtc)
            {
                _nextCleanupUtc = now.AddMinutes(1);
                foreach (var expired in _entries.Where(pair => pair.Value.Work?.IsCompleted == true
                             && pair.Value.ExpiresAtUtc <= now.Add(RenewalSkew)).Select(pair => pair.Key).ToArray())
                    _entries.Remove(expired);
            }
            if (_entries.TryGetValue(key, out var entry))
            {
                if (entry.Token is { Length: > 0 } token && entry.ExpiresAtUtc > now.Add(RenewalSkew))
                    return token;
                if (entry.Work is { IsCompleted: false } pending)
                    refresh = pending;
                else
                {
                    _entries.Remove(key);
                    refresh = StartRefreshAsync(key, factory);
                }
            }
            else
                refresh = StartRefreshAsync(key, factory);
        }
        return await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<string> StartRefreshAsync(string key,
        Func<CancellationToken, ValueTask<(string Token, DateTimeOffset ExpiresAtUtc)>> factory)
    {
        if (_entries.Count >= MaximumEntries)
        {
            var evictable = _entries.Where(pair => pair.Value.Work?.IsCompleted == true)
                .OrderBy(pair => pair.Value.ExpiresAtUtc).FirstOrDefault();
            // 不淘汰在途任务，否则相同凭据会重复刷新且绕过在途容量上限。
            if (evictable.Key is null) throw new InvalidOperationException("Provider token refresh capacity exceeded.");
            _entries.Remove(evictable.Key);
        }
        var entry = new Entry();
        _entries.Add(key, entry);
        entry.Work = RefreshAsync(key, entry, factory);
        // 所有等待者取消后仍由缓存持有任务；故障必须被观察，不能变成未观察异常。
        _ = entry.Work.ContinueWith(static task => { _ = task.Exception; },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return entry.Work;
    }

    private async Task<string> RefreshAsync(string key, Entry entry,
        Func<CancellationToken, ValueTask<(string Token, DateTimeOffset ExpiresAtUtc)>> factory)
    {
        var owner = entry.Owner!;
        try
        {
            // Transport 的 HTTP 超时同为 30 秒；取消令牌由刷新任务拥有，不绑定首个请求。
            var issued = await factory(owner.Token).ConfigureAwait(false);
            owner.Token.ThrowIfCancellationRequested();
            lock (_sync)
            {
                entry.Token = issued.Token;
                entry.ExpiresAtUtc = issued.ExpiresAtUtc;
            }
            return issued.Token;
        }
        catch
        {
            lock (_sync) _entries.Remove(key);
            throw;
        }
        finally
        {
            lock (_sync) entry.Owner = null;
            owner.Dispose();
        }
    }

    /// <summary>取消在途刷新并等待完成；宿主关闭时调用，避免 Transport 在进程退出后仍挂起。</summary>
    public async ValueTask DisposeAsync()
    {
        Entry[] pending;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            pending = _entries.Values.Where(entry => entry.Work is { IsCompleted: false }).ToArray();
            _entries.Clear();
        }
        foreach (var entry in pending)
        {
            try { if (entry.Owner is { } owner) await owner.CancelAsync().ConfigureAwait(false); }
            catch (ObjectDisposedException) { /* 刷新已并发完成并释放其令牌。 */ }
        }
        try { await Task.WhenAll(pending.Select(entry => entry.Work!)).ConfigureAwait(false); }
        catch { /* 每个任务已向等待者传播或由故障观察器读取，不阻断宿主释放。 */ }
    }

    private sealed class Entry
    {
        public string? Token;
        public DateTimeOffset ExpiresAtUtc;
        public Task<string>? Work;
        public CancellationTokenSource? Owner = new(TimeSpan.FromSeconds(30));
    }
}
