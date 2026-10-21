using Full.NET.Abstractions.Time;

namespace Full.NET.Hosting.Background;

/// <summary>独立续租与本地租约截止时间共同约束执行；迟到的续租成功不能恢复已经取消的工作。</summary>
public sealed class ExecutionLeaseMonitor : IAsyncDisposable
{
    private readonly IClock _clock;
    private readonly Func<DateTimeOffset, CancellationToken, Task<DateTimeOffset?>> _renew;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _deadline;
    private readonly CancellationTokenSource _work;
    private readonly CancellationTokenSource _stop;
    private readonly Task _monitor;
    private long _expiresAtUtcTicks;

    /// <summary>以领取时的租约和总执行预算启动监视；续租委托必须使用独立数据库作用域。</summary>
    /// <param name="expiresAtUtc">领取时确认的到期时间。</param>
    /// <param name="interval">续租间隔。</param>
    /// <param name="maximumDuration">不可通过续租延长的总执行预算。</param>
    /// <param name="clock">权威业务时钟。</param>
    /// <param name="renew">返回本轮 SQL 参数对应的新到期时间；失租返回空。</param>
    /// <param name="cancellationToken">调用链取消。</param>
    public ExecutionLeaseMonitor(DateTimeOffset expiresAtUtc, TimeSpan interval, TimeSpan maximumDuration,
        IClock clock, Func<DateTimeOffset, CancellationToken, Task<DateTimeOffset?>> renew, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumDuration, TimeSpan.Zero);
        _expiresAtUtcTicks = expiresAtUtc.UtcTicks;
        _clock = clock;
        _renew = renew;
        _interval = interval;
        _deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _deadline.CancelAfter(maximumDuration);
        _work = CancellationTokenSource.CreateLinkedTokenSource(_deadline.Token);
        var remaining = expiresAtUtc - clock.UtcNow;
        if (remaining <= TimeSpan.Zero) _work.Cancel();
        else _work.CancelAfter(remaining);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(_work.Token);
        _monitor = MonitorAsync();
    }

    /// <summary>所有文件、业务处理、上传和进度提交共同使用的工作令牌。</summary>
    public CancellationToken Token => _work.Token;

    /// <summary>最后确认的期限，终态事务写后仍须以新鲜业务时钟核对。</summary>
    public DateTimeOffset ExpiresAtUtc => new(Interlocked.Read(ref _expiresAtUtcTicks), TimeSpan.Zero);

    /// <summary>终态提交前先停止并收敛续租，工作令牌的到期保护继续有效。</summary>
    public async Task StopRenewalAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _monitor.ConfigureAwait(false);
    }

    private async Task MonitorAsync()
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
                if (_stop.IsCancellationRequested) return;
                var expires = await _renew(ExpiresAtUtc, _stop.Token).ConfigureAwait(false);
                if (_stop.IsCancellationRequested) return;
                if (expires is null || expires <= _clock.UtcNow)
                {
                    await _work.CancelAsync().ConfigureAwait(false);
                    return;
                }
                // 本地旧租约已到期会使 _work 永久取消；只接受尚未到期且实际续租成功的期限。
                if (!_work.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _expiresAtUtcTicks, expires.Value.UtcTicks);
                    _work.CancelAfter(expires.Value - _clock.UtcNow);
                }
            }
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception)
        {
            await _work.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>等待续租停止及其独立资源释放，禁止遗留后台数据库操作。</summary>
    public async ValueTask DisposeAsync()
    {
        await StopRenewalAsync().ConfigureAwait(false);
        _stop.Dispose();
        _work.Dispose();
        _deadline.Dispose();
    }
}
