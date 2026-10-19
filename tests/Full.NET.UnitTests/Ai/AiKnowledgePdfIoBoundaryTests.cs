using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Data.Abstractions;
using System.Security.Cryptography;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：Knowledge Pdf Io Boundary。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AiKnowledgePdfIoBoundaryTests
{
    [TestMethod]
    public async Task Synchronous_port_runs_on_a_dedicated_thread_and_does_not_block_its_caller()
    {
        await using var scheduler = new AiKnowledgePdfIoScheduler(1, 1);
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        var pending = scheduler.RunAsync(() =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(10));
            return Task.FromResult(Thread.CurrentThread.IsThreadPoolThread);
        });
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { release.Set(); }
        Assert.IsFalse(await pending.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [TestMethod]
    public async Task Capacity_is_held_until_async_work_completes_and_then_can_be_reused()
    {
        await using var scheduler = new AiKnowledgePdfIoScheduler(1, 1);
        var first = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = Signal(); var starts = 0; var rejectedStarted = false;
        Task<int> Start(Task<int> work) { if (Interlocked.Increment(ref starts) == 2) entered.TrySetResult(); return work; }
        var one = scheduler.RunAsync(() => Start(first.Task));
        var two = scheduler.RunAsync(() => Start(second.Task));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Assert.ThrowsAsync<IOException>(() => scheduler.RunAsync(() => { rejectedStarted = true; return Task.FromResult(0); }));
            Assert.IsFalse(rejectedStarted);
            first.SetResult(1); Assert.AreEqual(1, await one);
            Assert.AreEqual(3, await scheduler.RunAsync(() => Task.FromResult(3)));
        }
        finally { first.TrySetResult(1); second.TrySetResult(2); await Task.WhenAll(one, two); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Port_exception_is_preserved_and_does_not_stop_the_worker(bool asynchronous)
    {
        await using var scheduler = new AiKnowledgePdfIoScheduler(1, 1);
        var error = new IOException("port failed");
        var caught = await Assert.ThrowsAsync<IOException>(() => scheduler.RunAsync<int>(() => asynchronous ? Task.FromException<int>(error) : throw error));
        Assert.AreSame(error, caught);
        Assert.AreEqual(7, await scheduler.RunAsync(() => Task.FromResult(7)));
    }

    [TestMethod]
    public async Task Cancellation_remains_cancelled_and_releases_capacity()
    {
        await using var scheduler = new AiKnowledgePdfIoScheduler(1, 1);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var task = scheduler.RunAsync(() => Task.FromCanceled<int>(cancellation.Token));
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        Assert.AreEqual(cancellation.Token, error.CancellationToken); Assert.IsTrue(task.IsCanceled);
        Assert.AreEqual(9, await scheduler.RunAsync(() => Task.FromResult(9)));
    }

    [TestMethod]
    public async Task Request_context_flows_per_operation_without_polluting_the_next_operation()
    {
        await using var scheduler = new AiKnowledgePdfIoScheduler(1, 1);
        var context = new AsyncLocal<string?>(); context.Value = "first";
        Assert.AreEqual("first", await scheduler.RunAsync(() => { var found = context.Value; context.Value = "changed"; return Task.FromResult(found); }));
        Assert.AreEqual("first", context.Value); context.Value = "second";
        Assert.AreEqual("second", await scheduler.RunAsync(() => Task.FromResult(context.Value)));
        Task<string?> suppressed;
        using (ExecutionContext.SuppressFlow())
        {
            var pending = scheduler.RunAsync(() => Task.FromResult<string?>(context.Value));
            // 抑制流不能跨 await；退出后再等待任务。
            context.Value = "third";
            suppressed = pending;
        }
        Assert.IsNull(await suppressed);
    }

    [TestMethod]
    public async Task Shutdown_rejects_new_work_and_waits_for_accepted_async_work()
    {
        var scheduler = new AiKnowledgePdfIoScheduler(1, 1);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = scheduler.RunAsync(() => release.Task);
        var stopping = scheduler.DisposeAsync().AsTask();
        try
        {
            Assert.IsFalse(stopping.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => scheduler.RunAsync(() => Task.FromResult(0)));
        }
        finally { release.TrySetResult(1); await accepted; await stopping; }
    }

    [TestMethod]
    public async Task Cleanup_lane_remains_available_when_operation_capacity_is_exhausted()
    {
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = Signal(); var count = 0;
        var pending = Enumerable.Range(0, 12).Select(_ => AiKnowledgePdfIoBoundary.RunAsync(() =>
        { if (Interlocked.Increment(ref count) == 12) entered.TrySetResult(); return release.Task; })).ToArray();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Assert.ThrowsAsync<IOException>(() => AiKnowledgePdfIoBoundary.RunAsync(() => Task.FromResult(0)));
            var cleaned = false;
            await AiKnowledgePdfIoBoundary.CleanupAsync(() => { cleaned = true; return Task.CompletedTask; }).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsTrue(cleaned);
        }
        finally { release.TrySetResult(0); await Task.WhenAll(pending); }
    }

    [TestMethod]
    public async Task Reserved_cleanup_starts_even_when_all_other_cleanup_capacity_is_occupied()
    {
        using var reservation = AiKnowledgePdfIoBoundary.ReserveCleanup();
        var release = Signal();
        var pending = Enumerable.Range(0, 11).Select(_ => AiKnowledgePdfIoBoundary.CleanupAsync(() => release.Task)).ToArray();
        try
        {
            await Assert.ThrowsAsync<IOException>(() => AiKnowledgePdfIoBoundary.CleanupAsync(() => Task.CompletedTask));
            var cleaned = false;
            await reservation.RunAsync(() => { cleaned = true; return Task.CompletedTask; });
            Assert.IsTrue(cleaned);
        }
        finally { release.TrySetResult(); await Task.WhenAll(pending); }
    }

    [TestMethod]
    public async Task Cleanup_is_reserved_before_resource_acquisition_or_chargeable_work()
    {
        var release = Signal();
        var pending = Enumerable.Range(0, 12).Select(_ => AiKnowledgePdfIoBoundary.CleanupAsync(() => release.Task)).ToArray();
        var started = false;
        try
        {
            Assert.Throws<IOException>(() => { using var reservation = AiKnowledgePdfIoBoundary.ReserveCleanup(); started = true; });
            Assert.IsFalse(started);
        }
        finally { release.TrySetResult(); await Task.WhenAll(pending); }
        using var recovered = AiKnowledgePdfIoBoundary.ReserveCleanup();
        await recovered.RunAsync(() => Task.CompletedTask);
    }

    [TestMethod]
    public async Task Late_cleanup_retains_its_reservation_after_the_outer_owner_exits()
    {
        var reservation = AiKnowledgePdfIoBoundary.ReserveCleanup();
        using var retained = reservation.Retain();
        reservation.Dispose();
        var release = Signal();
        var pending = Enumerable.Range(0, 11).Select(_ => AiKnowledgePdfIoBoundary.CleanupAsync(() => release.Task)).ToArray();
        try
        {
            await Assert.ThrowsAsync<IOException>(() => AiKnowledgePdfIoBoundary.CleanupAsync(() => Task.CompletedTask));
            await reservation.RunAsync(() => Task.CompletedTask);
            await Assert.ThrowsAsync<InvalidOperationException>(() => reservation.RunAsync(() => Task.CompletedTask));
        }
        finally { release.TrySetResult(); await Task.WhenAll(pending); }
    }

    [TestMethod]
    public async Task Shutdown_accepts_reserved_cleanup_and_waits_until_it_completes()
    {
        var scheduler = new AiKnowledgePdfIoScheduler(1, 1);
        using var reservation = scheduler.Reserve();
        var release = Signal();
        var stopping = scheduler.DisposeAsync().AsTask();
        try
        {
            Assert.IsFalse(stopping.IsCompleted);
            var cleanup = reservation.RunAsync(() => release.Task);
            Assert.IsFalse(stopping.IsCompleted);
            release.TrySetResult();
            await cleanup;
        }
        finally { release.TrySetResult(); reservation.Dispose(); await stopping; }
    }

    [TestMethod]
    public async Task Owned_source_uses_its_reserved_cleanup_when_the_lane_is_full()
    {
        using var reservation = AiKnowledgePdfIoBoundary.ReserveCleanup();
        var release = Signal();
        var pending = Enumerable.Range(0, 11).Select(_ => AiKnowledgePdfIoBoundary.CleanupAsync(() => release.Task)).ToArray();
        using var stream = new MemoryStream([1, 2, 3], false);
        try
        {
            using var bytes = await new AiKnowledgePdfSourceReader(new TransactionState())
                .ReadAsync(stream, Convert.ToHexStringLower(SHA256.HashData(new byte[] { 1, 2, 3 })), 3, default, reservation);
            Assert.IsNotNull(bytes);
            Assert.IsFalse(stream.CanRead);
        }
        finally { release.TrySetResult(); await Task.WhenAll(pending); }
    }

    private sealed class TransactionState : IDataTransactionState { public bool HasTransaction => false; }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
