using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using Full.NET.Caching.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Caching.Fusion;

/// <summary>先完整预检再执行，限制重复输入、实际目标及所有并发批次共同占用的 SDK 操作数。</summary>
internal sealed class FusionCacheBatchInvalidator : ICacheBatchInvalidator, IDisposable
{
    private readonly ICacheInvalidator _invalidator;
    private readonly ICachePolicyRegistry _policies;
    private readonly int _maxItems;
    private readonly int _maxBytes;
    private readonly int _maxConcurrency;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _slots;
    private readonly SemaphoreSlim _batches;

    public FusionCacheBatchInvalidator(ICacheInvalidator invalidator, ICachePolicyRegistry policies,
        IOptions<CacheOptions> options)
    {
        _invalidator = invalidator;
        _policies = policies;
        var limits = options.Value;
        // 与启动时建立的并发门采用同一份参数，不支持通过修改 Options 对象隐式热更新。
        _maxItems = limits.InvalidationMaxBatchItems;
        _maxBytes = limits.InvalidationMaxBatchBytes;
        _maxConcurrency = limits.InvalidationMaxConcurrency;
        _timeout = limits.InvalidationTimeout;
        _slots = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        _batches = new SemaphoreSlim(limits.InvalidationMaxConcurrentBatches, limits.InvalidationMaxConcurrentBatches);
    }

    public async ValueTask RemoveAsync(IEnumerable<CacheInvalidationTarget> targets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        cancellationToken.ThrowIfCancellationRequested();
        // 容量检查发生在枚举/复制前；调用方必须处理拒绝，不排入新的无界等待队列。
        if (!_batches.Wait(0))
        {
            CacheReliabilityTelemetry.RecordInvalidationBatchRejection("capacity");
            throw new InvalidOperationException("缓存失效并发批次已达上限。");
        }
        try
        {
            await ExecuteAsync(targets, cancellationToken).ConfigureAwait(false);
        }
        finally { _batches.Release(); }
    }

    private async ValueTask ExecuteAsync(IEnumerable<CacheInvalidationTarget> targets, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_timeout);
        var unique = new List<CacheInvalidationTarget>();
        var identities = new HashSet<CacheInvalidationTarget>();
        var inputCount = 0;
        long bytes = 0;
        // 输入重复也计入预检预算，不能用无限重复枚举绕过数量/字节限制。
        try
        {
            foreach (var target in targets)
            {
                budget.Token.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(target);
                if (++inputCount > _maxItems)
                    throw new ArgumentException("缓存失效批次超过数量上限。", nameof(targets));
                ArgumentException.ThrowIfNullOrWhiteSpace(target.EntryName);
                ArgumentException.ThrowIfNullOrWhiteSpace(target.Target);
                if (!Enum.IsDefined(target.Kind) || !Enum.IsDefined(target.Scope))
                    throw new ArgumentException("缓存失效批次包含未知类型或范围。", nameof(targets));
                if (target.EntryName.Length > 256 || target.Target.Length > 1024
                    || Encoding.UTF8.GetByteCount(target.Target) > 1024)
                    throw new ArgumentException("缓存失效目标超过长度上限。", nameof(targets));
                var policy = _policies.GetRequired(target.EntryName.Trim());
                bytes += Encoding.UTF8.GetByteCount(target.EntryName) + Encoding.UTF8.GetByteCount(target.Target);
                if (bytes > _maxBytes)
                    throw new ArgumentException("缓存失效批次超过 UTF-8 字节上限。", nameof(targets));
                // 条目名规范化，完整目标原样保留；不合并标签与键或不同传播范围。
                var normalized = target with { EntryName = policy.EntryName };
                if (identities.Add(normalized)) unique.Add(normalized);
            }
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            CacheReliabilityTelemetry.RecordInvalidationBatchRejection(cancellationToken.IsCancellationRequested ? "canceled" : "timeout");
            throw;
        }
        catch (Exception)
        {
            CacheReliabilityTelemetry.RecordInvalidationBatchRejection("invalid_input");
            throw;
        }

        var failures = new Exception?[unique.Count];
        var succeeded = false;
        try
        {
            try
            {
                await Parallel.ForEachAsync(Enumerable.Range(0, unique.Count),
                    new ParallelOptions { MaxDegreeOfParallelism = _maxConcurrency, CancellationToken = budget.Token },
                    async (index, token) =>
                    {
                        await _slots.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            var target = unique[index];
                            if (target.Kind == CacheInvalidationTargetKind.Key)
                                await _invalidator.RemoveAsync(target.EntryName, target.Target, target.Scope, token).ConfigureAwait(false);
                            else
                                await _invalidator.RemoveByTagAsync(target.EntryName, target.Target, target.Scope, token).ConfigureAwait(false);
                        }
                        catch (Exception exception) { failures[index] = exception; }
                        finally { _slots.Release(); }
                    }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested)
            {
                // 所有已启动操作结束后再传播；保留已经观察到的 SDK 失败，不由后来的超时覆盖。
            }
            var failure = failures.FirstOrDefault(exception => exception is not null);
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            budget.Token.ThrowIfCancellationRequested();
            succeeded = true;
        }
        finally
        {
            CacheReliabilityTelemetry.RecordInvalidationBatch(Stopwatch.GetElapsedTime(startedAt),
                inputCount, unique.Count, succeeded);
        }
    }

    /// <summary>宿主停止业务调用后由 DI 容器释放并发门；批次不持有后台任务。</summary>
    public void Dispose()
    {
        _slots.Dispose();
        _batches.Dispose();
    }
}
