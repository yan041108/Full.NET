using System.Collections.Concurrent;
using System.Threading.Channels;
using Confluent.Kafka;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 为每个 Kafka 分区建立容量为一的处理通道。同一分区只有一个在途 Handler，
/// 不同分区拥有独立 Reader，因此可以并行执行且不会共享 Scoped 事务状态。
/// </summary>
internal sealed class KafkaPartitionWorkScheduler : IAsyncDisposable
{
    private readonly Func<ConsumeResult<string, byte[]>, CancellationToken, Task<bool>> _processor;
    private readonly Channel<KafkaPartitionProcessingResult> _completions;
    private readonly ConcurrentDictionary<TopicPartition, PartitionLane> _lanes = new();
    private readonly ConcurrentDictionary<Task, byte> _laneTasks = new();
    private readonly CancellationTokenSource _shutdown = new();
    private int _disposed;

    public KafkaPartitionWorkScheduler(
        Func<ConsumeResult<string, byte[]>, CancellationToken, Task<bool>> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _processor = processor;
        _completions = Channel.CreateUnbounded<KafkaPartitionProcessingResult>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
    }

    public int InFlightCount => _lanes.Values.Count(lane => lane.IsBusy);

    public bool HasPendingCompletion => _completions.Reader.TryPeek(out _);

    internal int TrackedLaneTaskCount => _laneTasks.Count;

    public bool TrySchedule(
        ConsumeResult<string, byte[]> consumeResult,
        long assignmentEpoch)
    {
        ArgumentNullException.ThrowIfNull(consumeResult);
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        var lane = _lanes.GetOrAdd(
            consumeResult.TopicPartition,
            CreateLane);
        return lane.TrySchedule(new PartitionWorkItem(consumeResult, assignmentEpoch));
    }

    public bool TryReadCompletion(out KafkaPartitionProcessingResult result) =>
        _completions.Reader.TryRead(out result!);

    public ValueTask<KafkaPartitionProcessingResult> ReadCompletionAsync(
        CancellationToken cancellationToken) =>
        _completions.Reader.ReadAsync(cancellationToken);

    /// <summary>
    /// Rebalance 撤销分区时立即取消对应 Handler。完成通知仍携带旧分配代次，
    /// Consumer 循环必须用代次 Fence 丢弃迟到结果，禁止提交新 Owner 的 Offset。
    /// </summary>
    public void Revoke(TopicPartition topicPartition)
    {
        if (_lanes.TryRemove(topicPartition, out var lane))
        {
            lane.Cancel();
        }
    }

    public async Task<bool> StopAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _shutdown.Cancel();
        foreach (var lane in _lanes.Values)
        {
            lane.Cancel();
        }

        var allLanes = Task.WhenAll(_laneTasks.Keys.ToArray());
        try
        {
            await allLanes.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            _ = allLanes.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _shutdown.Cancel();
        foreach (var lane in _lanes.Values)
        {
            lane.Cancel();
        }

        var allLanes = Task.WhenAll(_laneTasks.Keys.ToArray());
        if (allLanes.IsCompleted)
        {
            FinalizeDispose(allLanes);
            return ValueTask.CompletedTask;
        }

        // StopAsync 已承担有界等待；若 Handler 忽略取消，释放路径只保留后台观察，
        // 禁止再次无限等待而绕过 ShutdownDrainSeconds。
        _ = allLanes.ContinueWith(
            static (completed, state) =>
                ((KafkaPartitionWorkScheduler)state!).FinalizeDispose(completed),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return ValueTask.CompletedTask;
    }

    private void FinalizeDispose(Task allLanes)
    {
        if (allLanes.IsFaulted)
        {
            // 每条 Lane 会把 Handler 异常转换为 completion；这里只观察基础设施异常，避免二次传播。
            _ = allLanes.Exception;
        }

        _completions.Writer.TryComplete();
        _shutdown.Dispose();
    }

    private PartitionLane CreateLane(TopicPartition topicPartition)
    {
        var lane = new PartitionLane(
            topicPartition,
            _processor,
            _completions.Writer,
            _shutdown.Token);
        if (!_laneTasks.TryAdd(lane.Completion, 0))
        {
            throw new InvalidOperationException("Kafka partition lane task was already tracked.");
        }

        _ = lane.Completion.ContinueWith(
            static (completed, state) =>
            {
                var tracked = (ConcurrentDictionary<Task, byte>)state!;
                tracked.TryRemove(completed, out _);
                if (completed.IsFaulted)
                {
                    // Lane 会转换业务处理异常；这里只观察通道等基础设施故障，避免形成未观察任务异常。
                    _ = completed.Exception;
                }
            },
            _laneTasks,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return lane;
    }

    private sealed class PartitionLane
    {
        private readonly Channel<PartitionWorkItem> _channel;
        private readonly Func<ConsumeResult<string, byte[]>, CancellationToken, Task<bool>> _processor;
        private readonly ChannelWriter<KafkaPartitionProcessingResult> _completionWriter;
        private readonly CancellationTokenSource _cancellation;
        private int _busy;

        public PartitionLane(
            TopicPartition topicPartition,
            Func<ConsumeResult<string, byte[]>, CancellationToken, Task<bool>> processor,
            ChannelWriter<KafkaPartitionProcessingResult> completionWriter,
            CancellationToken shutdownToken)
        {
            TopicPartition = topicPartition;
            _processor = processor;
            _completionWriter = completionWriter;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            _channel = Channel.CreateBounded<PartitionWorkItem>(
                new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false,
                });
            Completion = RunAsync();
        }

        public TopicPartition TopicPartition { get; }

        public Task Completion { get; }

        public bool IsBusy => Volatile.Read(ref _busy) != 0;

        public bool TrySchedule(PartitionWorkItem item)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                return false;
            }

            if (_channel.Writer.TryWrite(item))
            {
                return true;
            }

            Volatile.Write(ref _busy, 0);
            return false;
        }

        public void Cancel()
        {
            _channel.Writer.TryComplete();
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Lane 可能刚好在取消完成后释放 CTS；重复撤销不改变结果。
            }
        }

        private async Task RunAsync()
        {
            try
            {
                await foreach (var item in _channel.Reader.ReadAllAsync(_cancellation.Token))
                {
                    KafkaPartitionProcessingResult result;
                    try
                    {
                        var shouldCommit = await _processor(
                                item.ConsumeResult,
                                _cancellation.Token)
                            .ConfigureAwait(false);
                        result = new KafkaPartitionProcessingResult(
                            item.ConsumeResult,
                            item.AssignmentEpoch,
                            shouldCommit,
                            null);
                    }
                    catch (Exception exception)
                    {
                        result = new KafkaPartitionProcessingResult(
                            item.ConsumeResult,
                            item.AssignmentEpoch,
                            false,
                            exception);
                    }

                    // 分区仍由 Consumer Loop 保持 Pause；先释放 Lane 槽位，再发布完成命令，
                    // 可避免 Resume 后下一条消息与 busy 标记清理之间出现竞态。
                    Volatile.Write(ref _busy, 0);
                    await _completionWriter.WriteAsync(result, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                // 若取消发生在读取等待阶段，没有在途工作需要额外生成 completion。
            }
            finally
            {
                _cancellation.Dispose();
            }
        }
    }

    private sealed record PartitionWorkItem(
        ConsumeResult<string, byte[]> ConsumeResult,
        long AssignmentEpoch);
}

internal sealed record KafkaPartitionProcessingResult(
    ConsumeResult<string, byte[]> ConsumeResult,
    long AssignmentEpoch,
    bool ShouldCommit,
    Exception? Exception);
