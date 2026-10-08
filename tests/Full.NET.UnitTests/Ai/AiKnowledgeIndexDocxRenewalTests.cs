using Full.NET.Data.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiKnowledgeIndexDocxPlanningServiceTests
{
    [TestMethod]
    public async Task Bounded_renewal_refuses_noncooperative_read_after_independent_cleanup_limit()
    {
        using var f = new Fixture(false); using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeAuthorizeAsync = _ => { entered.TrySetResult(); return release.Task; };
        var work = f.Sessions.RenewHeldWithBoundedReadsAsync(f.Binding, f.TaskId, f.Lease, f.Session, cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancel.Cancel();
            await Assert.ThrowsAsync<IOException>(() => work.WaitAsync(TimeSpan.FromSeconds(7)));
            Assert.AreEqual(0, f.Commands);
        }
        finally { release.TrySetResult(); try { await work; } catch (IOException) { } }
    }

    [TestMethod]
    public async Task Bounded_renewal_waits_for_noncooperative_write_after_cancellation()
    {
        using var f = new Fixture(true); using var cancel = new CancellationTokenSource();
        f.Clock.UtcNow.Returns(f.Now.AddSeconds(10));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { entered.TrySetResult(); return release.Task; });
        var work = f.Sessions.RenewHeldWithBoundedReadsAsync(f.Binding, f.TaskId, f.Lease, f.Session, cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancel.Cancel();
            Assert.IsFalse(work.IsCompleted, "续租写入不能被放弃后留在后台提交"); release.TrySetResult(1);
            await Assert.ThrowsAsync<OperationCanceledException>(() => work);
        }
        finally { release.TrySetResult(1); try { await work; } catch (OperationCanceledException) { } }
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Bounded_renewal_rechecks_original_session_after_authorization_wait(bool tenant)
    {
        using var f = new Fixture(tenant);
        f.AfterAuthorize = () => f.Session = f.Session with { SecurityStamp = "changed" };
        Assert.IsFalse(await f.Sessions.RenewHeldWithBoundedReadsAsync(f.Binding, f.TaskId, f.Lease, f.FrozenSession, default));
        Assert.AreEqual(0, f.Commands);
    }
}
