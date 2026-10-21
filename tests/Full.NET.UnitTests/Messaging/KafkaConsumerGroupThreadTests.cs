using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

/// <summary>同步消费组必须独立启动，不占线程池，且宿主等待包含关闭结果。</summary>
[TestClass]
public sealed class KafkaConsumerGroupThreadTests
{
    [TestMethod]
    public async Task Blocked_first_group_does_not_block_other_group_or_use_thread_pool()
    {
        using var release = new ManualResetEventSlim();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = KafkaConsumerWorker.StartConsumerGroup(() =>
        {
            Assert.IsFalse(Thread.CurrentThread.IsThreadPoolThread);
            firstEntered.TrySetResult();
            release.Wait();
            return Task.CompletedTask;
        }, "first", default);
        var second = KafkaConsumerWorker.StartConsumerGroup(() =>
        {
            Assert.IsFalse(Thread.CurrentThread.IsThreadPoolThread);
            secondEntered.TrySetResult();
            return Task.CompletedTask;
        }, "second", default);
        try
        {
            await Task.WhenAll(firstEntered.Task, secondEntered.Task, second).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(first.IsCompleted);
        }
        finally { release.Set(); await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [TestMethod]
    public async Task Group_completion_waits_for_async_cleanup_and_preserves_failure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var group = KafkaConsumerWorker.StartConsumerGroup(() =>
        {
            entered.TrySetResult();
            return cleanup.Task;
        }, "cleanup", default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(group.IsCompleted);
        var failure = new InvalidOperationException("cleanup failed");
        cleanup.SetException(failure);
        Assert.AreSame(failure, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => group));
        Assert.AreSame(failure, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            KafkaConsumerWorker.StartConsumerGroup(() => throw failure, "startup", default)));
    }

    [TestMethod]
    public async Task Failed_group_cancels_siblings_and_waits_for_cleanup_before_reporting_failure()
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var marker = new InvalidOperationException("consumer failed");
        var supervised = KafkaConsumerWorker.SuperviseConsumerGroups([
            async token =>
            {
                running.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { stopped.TrySetResult(); }
            },
            _ => failure.Task
        ], default);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(5));
        failure.SetException(marker);
        Assert.AreSame(marker, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            supervised.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.IsTrue(stopped.Task.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task Precancelled_group_never_starts_consumer()
    {
        var called = false;
        var group = KafkaConsumerWorker.StartConsumerGroup(() => { called = true; return Task.CompletedTask; },
            "cancelled", new CancellationToken(true));
        await Assert.ThrowsAsync<OperationCanceledException>(() => group);
        Assert.IsFalse(called);
    }
}
