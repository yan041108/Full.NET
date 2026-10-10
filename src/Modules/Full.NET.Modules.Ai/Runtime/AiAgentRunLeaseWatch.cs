using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Agents.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>在独立数据库作用域内续租；用独立期限取消执行，并在提交前交接最新 CAS 版本。</summary>
/// <remarks>停止调度不会取消正在执行的续租；必须等待其确定结果后才能提交。忽略令牌的外部调用仍须等待返回，但晚到结果不能推进运行。</remarks>
internal sealed class AiAgentRunLeaseWatch : IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IClock _clock;
    private readonly TimeSpan _duration;
    private readonly DateTimeOffset _deadline;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _leaseDeadline;
    private readonly CancellationTokenSource _runDeadline;
    private readonly CancellationTokenSource _execution;
    private readonly Task _watch;
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private AgentRunLease _lease;

    /// <summary>监视已领取运行；租约和运行期限分别约束实际派发令牌。</summary>
    /// <param name="scopes">续租使用独立连接与事务状态的作用域工厂。</param>
    /// <param name="clock">持久化租约使用的 UTC 时钟。</param>
    /// <param name="lease">当前租约与 CAS 行版本。</param>
    /// <param name="deadline">运行冻结的绝对到期时间。</param>
    /// <param name="duration">每次续租的有效期。</param>
    /// <param name="logger">仅记录运行标识及续租故障，不记录模型输入。</param>
    /// <param name="callerToken">Worker 停止或调用方取消令牌。</param>
    internal AiAgentRunLeaseWatch(IServiceScopeFactory scopes, IClock clock, AgentRunLease lease,
        DateTimeOffset deadline, TimeSpan duration, ILogger logger, CancellationToken callerToken)
    {
        _scopes = scopes;
        _clock = clock;
        _lease = lease;
        _deadline = deadline;
        _duration = duration;
        _logger = logger;
        var now = clock.UtcNow;
        _leaseDeadline = new(Remaining(lease.ExpiresAtUtc, now));
        _runDeadline = new(Remaining(deadline, now));
        _execution = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _leaseDeadline.Token, _runDeadline.Token);
        _watch = WatchAsync();
    }

    /// <summary>三类运行共用的派发令牌；续租失败、静默到期及运行 deadline 都会取消。</summary>
    internal CancellationToken Token => _execution.Token;

    /// <summary>同时复核令牌与权威时钟，避免定时器尚未调度时继续提交。</summary>
    internal bool IsActive => !_execution.IsCancellationRequested
        && _clock.UtcNow < Volatile.Read(ref _lease).ExpiresAtUtc && _clock.UtcNow < _deadline;

    /// <summary>停止后续调度并等待正在执行的续租，返回可用于提交的最后确定版本。</summary>
    internal async Task<AgentRunLease> StopAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _watch.ConfigureAwait(false);
        return Volatile.Read(ref _lease);
    }

    /// <summary>节点提交与续租串行使用最新CAS版本；结果不明时取消执行，不继续派发或猜测提交失败。</summary>
    internal async Task<bool> CommitNodeAsync(Func<AgentRunLease, CancellationToken, Task<bool>> commit)
    {
        await _mutation.WaitAsync(Token).ConfigureAwait(false);
        try
        {
            if (!IsActive) return false;
            var lease = Volatile.Read(ref _lease);
            if (!await commit(lease, Token).ConfigureAwait(false))
            {
                await _execution.CancelAsync().ConfigureAwait(false);
                return false;
            }
            Volatile.Write(ref _lease, lease with { Version = lease.Version + 1 });
            if (IsActive) return true;
            await _execution.CancelAsync().ConfigureAwait(false);
            return false;
        }
        catch
        {
            await _execution.CancelAsync().ConfigureAwait(false);
            throw;
        }
        finally { _mutation.Release(); }
    }

    private async Task WatchAsync()
    {
        using var timer = new PeriodicTimer(_duration / 3);
        try
        {
            while (true)
            {
                // 停止只取消定时器等待；不能吞掉使用执行令牌的在途续租异常。
                try { if (!await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false)) return; }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
                await _mutation.WaitAsync(Token).ConfigureAwait(false);
                try
                {
                    if (!IsActive) { await _execution.CancelAsync().ConfigureAwait(false); return; }
                    var lease = Volatile.Read(ref _lease);
                    var started = _clock.UtcNow;
                    // 正常收尾只停止调度；若数据库已续租，必须先接收递增后的版本，不能用旧版本提交。
                    var renewed = await RenewAsync(lease, started, Token).ConfigureAwait(false);
                    var expires = started + _duration;
                    if (!renewed || _execution.IsCancellationRequested || _clock.UtcNow >= lease.ExpiresAtUtc
                        || _clock.UtcNow >= expires || _clock.UtcNow >= _deadline)
                    {
                        await _execution.CancelAsync().ConfigureAwait(false);
                        return;
                    }
                    Volatile.Write(ref _lease, lease with { Version = lease.Version + 1, ExpiresAtUtc = expires });
                    _leaseDeadline.CancelAfter(Remaining(expires, _clock.UtcNow));
                }
                finally { _mutation.Release(); }
            }
        }
        catch (OperationCanceledException) when (_execution.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Agent run {RunId} lease renewal failed.", _lease.RunId);
            await _execution.CancelAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> RenewAsync(AgentRunLease lease, DateTimeOffset now, CancellationToken token)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        // 后台全局语句通过已领取的 RunId、Worker、Epoch 与 Version 锚定运行，不能共享业务作用域连接。
        tenant.SetHost();
        try
        {
            return await scope.ServiceProvider.GetRequiredService<AiAgentRunStore>()
                .RenewAsync(lease, now, _duration, token).ConfigureAwait(false);
        }
        finally { tenant.Clear(); }
    }

    private static TimeSpan Remaining(DateTimeOffset expires, DateTimeOffset now) =>
        expires > now ? expires - now : TimeSpan.Zero;

    /// <summary>先收敛监视任务，再释放所有期限与链接令牌。</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _execution.Dispose();
        _leaseDeadline.Dispose();
        _runDeadline.Dispose();
        _stop.Dispose();
        _mutation.Dispose();
    }
}
