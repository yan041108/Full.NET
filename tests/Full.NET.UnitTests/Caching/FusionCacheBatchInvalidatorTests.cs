using System.Collections.Concurrent;
using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Caching;

/// <summary>验证真实批次边界：完整预检、资源限额和部分失败后的有界清理。</summary>
[TestClass]
public sealed class FusionCacheBatchInvalidatorTests
{
    [TestMethod]
    [DataRow("count")]
    [DataRow("bytes")]
    [DataRow("kind")]
    [DataRow("scope")]
    [DataRow("entry")]
    [DataRow("utf8")]
    public async Task Invalid_batch_rejects_before_any_target_is_removed(string scenario)
    {
        var limits = new CacheOptions();
        var valid = Target("valid");
        CacheInvalidationTarget[] input = [valid, Target("next")];
        switch (scenario)
        {
            case "count": limits.InvalidationMaxBatchItems = 1; input = [valid, valid]; break;
            case "bytes": limits.InvalidationMaxBatchBytes = 1; break;
            case "kind": input[1] = input[1] with { Kind = (CacheInvalidationTargetKind)99 }; break;
            case "scope": input[1] = input[1] with { Scope = (CacheInvalidationScope)99 }; break;
            case "entry": input[1] = input[1] with { EntryName = "unknown-entry" }; break;
            case "utf8": input[1] = Target(new string('中', 342)); break;
        }
        var recorder = new RecordingInvalidator();
        using var batch = Create(recorder, limits);
        Exception? failure = null;
        try { await batch.RemoveAsync(input); }
        catch (Exception exception) { failure = exception; }
        Assert.IsNotNull(failure, "非法输入必须被拒绝。");
        Assert.HasCount(0, recorder.Calls.ToArray(), "后面的非法目标不能造成前面目标被部分删除。");
        // 预检失败也必须释放接纳槽；后续合法空批次仍能完成。
        await batch.RemoveAsync([]);
    }

    [TestMethod]
    public async Task Deduplication_preserves_kind_scope_and_full_tenant_environment_identity()
    {
        var recorder = new RecordingInvalidator();
        using var batch = Create(recorder);
        var key = Target("prod:tenant-a:key");
        await batch.RemoveAsync([
            key, key with { EntryName = $" {key.EntryName.ToUpperInvariant()} " },
            key with { Kind = CacheInvalidationTargetKind.Tag },
            key with { Scope = CacheInvalidationScope.AllLayersSynchronous },
            Target("prod:tenant-b:key"), Target("staging:tenant-a:key")
        ]);
        Assert.HasCount(5, recorder.Calls.ToArray());
        Assert.HasCount(1, recorder.Calls.Where(item => item == key).ToArray());
    }

    [TestMethod]
    public async Task Failures_do_not_skip_other_targets_and_preserve_first_input_exception()
    {
        var secondFailed = Signal();
        var original = new InvalidOperationException("first-input-failure");
        var recorder = new RecordingInvalidator(async (target, token) =>
        {
            if (target.Target == "first")
            {
                await secondFailed.Task.WaitAsync(token);
                throw original;
            }
            if (target.Target == "second")
            {
                secondFailed.TrySetResult();
                throw new ArgumentException("earlier-completion");
            }
        });
        using var batch = Create(recorder);
        var observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await batch.RemoveAsync([Target("first"), Target("second"), Target("third")]));
        Assert.AreSame(original, observed);
        StringAssert.Contains(observed.StackTrace!, nameof(Failures_do_not_skip_other_targets_and_preserve_first_input_exception));
        Assert.HasCount(3, recorder.Calls.ToArray());
    }

    [TestMethod]
    public async Task All_batches_share_the_same_sdk_concurrency_budget()
    {
        var entered = Signal();
        var release = Signal();
        var active = 0;
        var peak = 0;
        var recorder = new RecordingInvalidator(async (_, token) =>
        {
            var current = Interlocked.Increment(ref active);
            InterlockedExtensionsMax(ref peak, current);
            entered.TrySetResult();
            try { await release.Task.WaitAsync(token); }
            finally { Interlocked.Decrement(ref active); }
        });
        using var batch = Create(recorder, new CacheOptions { InvalidationMaxConcurrency = 1 });
        var first = batch.RemoveAsync([Target("one"), Target("two")]).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = batch.RemoveAsync([Target("three"), Target("four")]).AsTask();
        try
        {
            // 保持首个操作阻塞，给第二批次竞争 SDK 槽位的机会。
            await Task.Delay(75);
            Assert.HasCount(1, recorder.Calls.ToArray());
        }
        finally { release.TrySetResult(); await Task.WhenAll(first, second); }
        Assert.AreEqual(1, peak);
        Assert.HasCount(4, recorder.Calls.ToArray());
    }

    [TestMethod]
    public async Task Batch_capacity_rejects_without_enumerating_then_recovers()
    {
        var entered = Signal();
        var release = Signal();
        var recorder = new RecordingInvalidator(async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        });
        using var batch = Create(recorder, new CacheOptions { InvalidationMaxConcurrentBatches = 1 });
        var first = batch.RemoveAsync([Target("first")]).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var enumerated = false;
        IEnumerable<CacheInvalidationTarget> ForbiddenEnumeration()
        {
            enumerated = true;
            yield return Target("forbidden");
        }
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
                await batch.RemoveAsync(ForbiddenEnumeration()).AsTask().WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.IsFalse(enumerated);
        }
        finally { release.TrySetResult(); await first; }
        await batch.RemoveAsync([Target("after-release")]);
        Assert.HasCount(2, recorder.Calls.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_or_total_deadline_releases_slots_and_waits_for_started_work(bool timeout)
    {
        var entered = Signal();
        var ended = false;
        var recorder = new RecordingInvalidator(async (target, token) =>
        {
            if (target.Target != "blocked") return;
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { ended = true; }
        });
        using var batch = Create(recorder, new CacheOptions
        {
            InvalidationMaxConcurrency = 1,
            InvalidationMaxConcurrentBatches = 1,
            InvalidationTimeout = timeout ? TimeSpan.FromMilliseconds(150) : TimeSpan.FromSeconds(5)
        });
        using var cancellation = new CancellationTokenSource();
        var pending = batch.RemoveAsync([Target("blocked"), Target("queued")], cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!timeout) cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(ended, "返回取消前须等待已启动的 SDK 操作退出。");
        Assert.HasCount(1, recorder.Calls.ToArray());
        await batch.RemoveAsync([Target("after-cancel")]);
        Assert.HasCount(2, recorder.Calls.ToArray());
    }

    [TestMethod]
    public async Task Mutating_options_after_construction_cannot_change_live_resource_limits()
    {
        var limits = new CacheOptions { InvalidationMaxBatchItems = 1 };
        var recorder = new RecordingInvalidator();
        using var batch = Create(recorder, limits);
        limits.InvalidationMaxBatchItems = 100;
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await batch.RemoveAsync([Target("one"), Target("two")]));
        Assert.HasCount(0, recorder.Calls.ToArray());
    }

    private static FusionCacheBatchInvalidator Create(RecordingInvalidator recorder, CacheOptions? limits = null)
    {
        limits ??= new CacheOptions();
        return new FusionCacheBatchInvalidator(recorder, CachePolicyRegistry.Create(limits), Options.Create(limits));
    }

    private static CacheInvalidationTarget Target(string key) => new(CacheEntryNames.GridPreference, key,
        CacheInvalidationTargetKind.Key, CacheInvalidationScope.CurrentNodeOnly);

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void InterlockedExtensionsMax(ref int value, int candidate)
    {
        int previous;
        do { previous = Volatile.Read(ref value); if (previous >= candidate) return; }
        while (Interlocked.CompareExchange(ref value, candidate, previous) != previous);
    }

    /// <summary>记录真正进入单目标失效边界的调用，注入可控的在途操作而不模拟批次实现。</summary>
    private sealed class RecordingInvalidator(Func<CacheInvalidationTarget, CancellationToken, Task>? execute = null)
        : ICacheInvalidator
    {
        public ConcurrentQueue<CacheInvalidationTarget> Calls { get; } = new();
        public ValueTask RemoveAsync(string entryName, string key, CacheInvalidationScope scope, CancellationToken ct = default) =>
            Run(new(entryName, key, CacheInvalidationTargetKind.Key, scope), ct);
        public ValueTask RemoveByTagAsync(string entryName, string tag, CacheInvalidationScope scope, CancellationToken ct = default) =>
            Run(new(entryName, tag, CacheInvalidationTargetKind.Tag, scope), ct);
        private async ValueTask Run(CacheInvalidationTarget target, CancellationToken token)
        {
            Calls.Enqueue(target);
            if (execute is not null) await execute(target, token);
        }
    }
}
