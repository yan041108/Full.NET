using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Background;

namespace Full.NET.UnitTests.Hosting;

/// <summary>独立租约生命周期取消和迟到续租回归。</summary>
[TestClass]
public sealed class ExecutionLeaseMonitorTests
{
    [TestMethod]
    public async Task Failed_renewal_cancels_work()
    {
        await using var lease = new ExecutionLeaseMonitor(DateTimeOffset.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(2), new SystemClock(), (_, _) => Task.FromResult<DateTimeOffset?>(null), CancellationToken.None);
        Assert.IsTrue(lease.Token.IsCancellationRequested);
    }
    [TestMethod]
    public async Task Late_renewal_cannot_revive_expired_work()
    {
        var renewal = new TaskCompletionSource<DateTimeOffset?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var initial = DateTimeOffset.UtcNow.AddMilliseconds(100);
        await using var lease = new ExecutionLeaseMonitor(initial, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1),
            new SystemClock(), (_, _) => renewal.Task, CancellationToken.None);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lease.Token.Register(() => canceled.TrySetResult());
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        renewal.SetResult(DateTimeOffset.UtcNow.AddMinutes(1));
        await lease.StopRenewalAsync();
        Assert.IsTrue(lease.Token.IsCancellationRequested);
        Assert.AreEqual(initial, lease.ExpiresAtUtc);
    }
    [TestMethod]
    public async Task Caller_cancellation_is_forwarded_to_renewal_and_work()
    {
        using var caller = new CancellationTokenSource();
        await using var lease = new ExecutionLeaseMonitor(DateTimeOffset.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(2), new SystemClock(), async (_, token) => { await Task.Delay(Timeout.Infinite, token); return null; }, caller.Token);
        await caller.CancelAsync(); await lease.StopRenewalAsync();
        Assert.IsTrue(lease.Token.IsCancellationRequested);
    }
    [TestMethod]
    public async Task Stopping_renewal_keeps_expiration_guard_active()
    {
        var expiry = DateTimeOffset.UtcNow.AddMilliseconds(100);
        await using var lease = new ExecutionLeaseMonitor(expiry, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2),
            new SystemClock(), (_, _) => Task.FromResult<DateTimeOffset?>(expiry), CancellationToken.None);
        await lease.StopRenewalAsync();
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lease.Token.Register(() => canceled.TrySetResult());
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(lease.Token.IsCancellationRequested);
    }
}
