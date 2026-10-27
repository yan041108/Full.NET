using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Full.NET.Realtime.SignalR;

/// <summary>每个 API 实例管理自己的连接；各节点独立重查同一权威源，不依赖客户端或撤销通知送达。</summary>
public interface IRealtimeConnectionLeases
{
    /// <summary>验证连接并建立有界授权租约。</summary>
    /// <param name="context">当前服务端连接上下文。</param>
    Task<bool> RegisterAsync(HubCallerContext context);
    /// <summary>连接断开后释放其租约及身份快照。</summary>
    /// <param name="connectionId">连接标识。</param>
    void Remove(string connectionId);
}

/// <summary>授权租约最长三十秒；扫描拥堵或读取故障不能无限延长旧授权。</summary>
internal sealed class RealtimeConnectionLeases(IServiceScopeFactory scopes, TimeProvider time)
    : IRealtimeConnectionLeases, IDisposable
{
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, Lease> connections = new(StringComparer.Ordinal);

    public async Task<bool> RegisterAsync(HubCallerContext context)
    {
        if (context.User is null) return false;
        var principal = context.User.Clone();
        if (!await AuthorizeAsync(principal, context.ConnectionAborted).ConfigureAwait(false)) return false;
        var lease = new Lease(principal, context.Abort, time);
        if (!connections.TryAdd(context.ConnectionId, lease)) { lease.Dispose(); return false; }
        // 连接可能在初次权威读取期间断开；不能把已经断开的连接留给后台扫描。
        if (context.ConnectionAborted.IsCancellationRequested) Remove(context.ConnectionId);
        return !context.ConnectionAborted.IsCancellationRequested;
    }

    public void Remove(string connectionId)
    {
        if (connections.TryRemove(connectionId, out var lease)) lease.Dispose();
    }

    internal Task RevalidateAsync(CancellationToken token) => Parallel.ForEachAsync(connections.ToArray(),
        new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = token }, async (entry, ct) =>
        {
            if (await AuthorizeAsync(entry.Value.Principal, ct).ConfigureAwait(false)) entry.Value.Renew();
            else { entry.Value.Abort(); Remove(entry.Key); }
        });

    private async Task<bool> AuthorizeAsync(ClaimsPrincipal principal, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var authorized = await scope.ServiceProvider.GetRequiredService<IRealtimeConnectionAuthorizer>()
                .IsAuthorizedAsync(principal, deadline.Token).ConfigureAwait(false);
            return authorized && !deadline.IsCancellationRequested;
        }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        foreach (var id in connections.Keys) { if (connections.TryGetValue(id, out var lease)) lease.Abort(); Remove(id); }
    }

    /// <summary>过期与续租串行化；过期后的迟到读取不得复活连接。</summary>
    private sealed class Lease : IDisposable
    {
        private readonly object gate = new();
        private readonly Action abort;
        private readonly ITimer timer;
        private bool ended;
        public ClaimsPrincipal Principal { get; }
        public Lease(ClaimsPrincipal principal, Action abort, TimeProvider time)
        {
            Principal = principal; this.abort = abort;
            timer = time.CreateTimer(_ => Abort(), null, LeaseDuration, Timeout.InfiniteTimeSpan);
        }
        public void Renew() { lock (gate) { if (!ended) timer.Change(LeaseDuration, Timeout.InfiniteTimeSpan); } }
        public void Abort() { lock (gate) { if (ended) return; ended = true; abort(); } }
        public void Dispose() { lock (gate) { ended = true; timer.Dispose(); } }
    }
}

/// <summary>轮询只负责续租；独立的连接租约计时器在扫描超时或拥堵时仍会关闭失效连接。</summary>
internal sealed class RealtimeConnectionRevalidationWorker(RealtimeConnectionLeases leases) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await leases.RevalidateAsync(stoppingToken).ConfigureAwait(false);
    }
}
