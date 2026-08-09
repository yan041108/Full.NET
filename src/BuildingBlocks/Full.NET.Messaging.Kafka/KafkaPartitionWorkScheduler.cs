using System.Collections.Concurrent;
using System.Threading.Channels;
using Confluent.Kafka;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 按 Kafka 分区隔离处理状态，并在分区内按稳定业务 Key 分槽并行。
/// 同一 Key 始终进入同一单 Reader 槽，不同槽可以并行，连续提交水位仍由 Poll Loop 统一裁决。
/// </summary>
internal sealed class KafkaPartitionWorkScheduler : IAsyncDisposable
{
    private readonly Func<ConsumeResult<string, byte[]>, CancellationToken, Task<bool>> _processor;
    private readonly Channel<KafkaPartitionProcessingResult> _completions;
    private readonly ConcurrentDictionary<TopicPartition, PartitionLaneSet> _lanes = new();
    private readonly ConcurrentDictionary<Task, byte> _laneTasks = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly KafkaConsumerBufferPressure _globalPressure;
    private readonly int _partitionHighWatermark;
    private readonly int _partitionLowWatermark;
    private readonly int _slotCount;
    private readonly Action<long, int, int>? _onProcessingStateChanged;
    private int _activeHandlers;
    private int _disposed;
    private long _processingStateSequence;

    public KafkaPartitionWorkScheduler(
        Func<ConsumeResult<string, byte[]>, CancellationToken, Task<bool>> processor)
        : this(processor, new KafkaMessagingOptions())
    {
    }

    public KafkaPartitionWorkScheduler(
        Func<ConsumeResult<string, byte[]>, CancellationToken, Task<bool>> processor,
        KafkaMessagingOptions options,
        Action<long, int, int>? onProcessingStateChanged = null)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(options);
        _processor = processor;
        _globalPressure = new KafkaConsumerBufferPressure(
            options.ConsumerBufferHighWatermark,
            options.ConsumerBufferLowWatermark);
        _partitionHighWatermark = options.PartitionBufferHighWatermark;
        _partitionLowWatermark = options.PartitionBufferLowWatermark;
        _slotCount = options.PartitionKeyConcurrencySlots;
        _onProcessingStateChanged = onProcessingStateChanged;
        _completions = Channel.CreateUnbounded<KafkaPartitionProcessingResult>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
    }

    public int InFlightCount => _globalPressure.Depth;

    public int ActiveHandlerCount => Volatile.Read(ref _activeHandlers);

    public int BufferDepth => Math.Max(0, InFlightCount - ActiveHandlerCount);

    public bool HasPendingCompletion => _completions.Reader.TryPeek(out _);

    public bool ShouldPauseGlobally => _globalPressure.ShouldPause;

    public bool ShouldResumeGlobally => _globalPressure.ShouldResume;

    internal int TrackedLaneTaskCount => _laneTasks.Count;

    public int GetPartitionDepth(TopicPartition partition) =>
        _lanes.TryGetValue(partition, out var lane) ? lane.Depth : 0;

    public bool ShouldPausePartition(TopicPartition partition) =>
        _lanes.TryGetValue(partition, out var lane) && lane.ShouldPause;

    public bool ShouldResumePartition(TopicPartition partition) =>
        !_lanes.TryGetValue(partition, out var lane) || lane.ShouldResume;

    public bool TrySchedule(
        ConsumeResult<string, byte[]> consumeResult,
        long assignmentEpoch) =>
        TrySchedule(consumeResult, assignmentEpoch, out _);

    public bool TrySchedule(
        ConsumeResult<string, byte[]> consumeResult,
        long assignmentEpoch,
        out KafkaSchedulePressure pressure)
    {
        ArgumentNullException.ThrowIfNull(consumeResult);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        pressure = default;

        if (!_globalPressure.TryAccept())
        {
            return false;
        }

        var reachedGlobalHighWatermark = _globalPressure.ShouldPause;
        var lane = _lanes.GetOrAdd(consumeResult.TopicPartition, CreateLane);
        if (lane.TrySchedule(
                new PartitionWorkItem(consumeResult, assignmentEpoch),
                out var reachedPartitionHighWatermark))
        {
            NotifyProcessingStateChanged();
            pressure = new KafkaSchedulePressure(
                reachedGlobalHighWatermark,
                reachedPartitionHighWatermark);
            return true;
        }

        _globalPressure.OnCompleted();
        NotifyProcessingStateChanged();
        return false;
    }

    public bool TryReadCompletion(out KafkaPartitionProcessingResult result) =>
        _completions.Reader.TryRead(out result!);

    public ValueTask<KafkaPartitionProcessingResult> ReadCompletionAsync(
        CancellationToken cancellationToken) =>
        _completions.Reader.ReadAsync(cancellationToken);

    /// <summary>
    /// 撤销或失败回退分区时，取消全部槽以及仍在排队的消息；旧代次完成结果由 Consumer Fence 丢弃。
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
            ObserveLater(allLanes);
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
        }
        else
        {
            _ = allLanes.ContinueWith(
                static (completed, state) =>
                    ((KafkaPartitionWorkScheduler)state!).FinalizeDispose(completed),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return ValueTask.CompletedTask;
    }

    private PartitionLaneSet CreateLane(TopicPartition topicPartition)
    {
        var lane = new PartitionLaneSet(
            topicPartition,
            _processor,
            _completions.Writer,
            _globalPressure,
            _partitionHighWatermark,
            _partitionLowWatermark,
            _slotCount,
            () =>
            {
                Interlocked.Increment(ref _activeHandlers);
                NotifyProcessingStateChanged();
            },
            () =>
            {
                Interlocked.Decrement(ref _activeHandlers);
                NotifyProcessingStateChanged();
            },
            NotifyProcessingStateChanged,
            _shutdown.Token);
        if (!_laneTasks.TryAdd(lane.Completion, 0))
        {
            lane.Cancel();
            throw new InvalidOperationException("Kafka partition lane task was already tracked.");
        }

        _ = lane.Completion.ContinueWith(
            static (completed, state) =>
            {
                var tracked = (ConcurrentDictionary<Task, byte>)state!;
                tracked.TryRemove(completed, out _);
                if (completed.IsFaulted)
                {
                    _ = completed.Exception;
                }
            },
            _laneTasks,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return lane;
    }

    private void NotifyProcessingStateChanged()
    {
        try
        {
            var sequence = Interlocked.Increment(ref _processingStateSequence);
            _onProcessingStateChanged?.Invoke(sequence, ActiveHandlerCount, BufferDepth);
        }
        catch (Exception)
        {
            // 遥测回调是旁路，不能改变背压、完成通知或 Offset 语义。
        }
    }

    private void FinalizeDispose(Task allLanes)
    {
        if (allLanes.IsFaulted)
        {
            _ = allLanes.Exception;
        }

        _completions.Writer.TryComplete();
        _shutdown.Dispose();
    }

    private static void ObserveLater(Task task) =>
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private sealed class PartitionLaneSet
    {
        private readonly Channel<PartitionWorkItem>[] _channels;
        private readonly Func<ConsumeResult<string, byte[]>, CancellationToken, Task<bool>> _processor;
        private readonly ChannelWriter<KafkaPartitionProcessingResult> _completionWriter;
        private readonly KafkaConsumerBufferPressure _globalPressure;
        private readonly KafkaConsumerBufferPressure _localPressure;
        private readonly CancellationTokenSource _cancellation;
        private readonly Action _onHandlerStarted;
        private readonly Action _onHandlerStopped;
        private readonly Action _onStateChanged;
        private int _remainingSlots;
        private int _cancelled;

        public PartitionLaneSet(
            TopicPartition topicPartition,
            Func<ConsumeResult<string, byte[]>, CancellationToken, Task<bool>> processor,
            ChannelWriter<KafkaPartitionProcessingResult> completionWriter,
            KafkaConsumerBufferPressure globalPressure,
            int highWatermark,
            int lowWatermark,
            int slotCount,
            Action onHandlerStarted,
            Action onHandlerStopped,
            Action onStateChanged,
            CancellationToken shutdownToken)
        {
            TopicPartition = topicPartition;
            _processor = processor;
            _completionWriter = completionWriter;
            _globalPressure = globalPressure;
            _localPressure = new KafkaConsumerBufferPressure(highWatermark, lowWatermark);
            _onHandlerStarted = onHandlerStarted;
            _onHandlerStopped = onHandlerStopped;
            _onStateChanged = onStateChanged;
            _remainingSlots = slotCount;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            _channels = Enumerable.Range(0, slotCount)
                .Select(_ => Channel.CreateBounded<PartitionWorkItem>(
                    new BoundedChannelOptions(highWatermark)
                    {
                        SingleReader = true,
                        SingleWriter = true,
                        FullMode = BoundedChannelFullMode.Wait,
                        AllowSynchronousContinuations = false,
                    }))
                .ToArray();
            Completion = Task.WhenAll(_channels.Select(RunSlotAsync));
        }

        public TopicPartition TopicPartition { get; }

        public Task Completion { get; }

        public int Depth => _localPressure.Depth;

        public bool ShouldPause => _localPressure.ShouldPause;

        public bool ShouldResume => _localPressure.ShouldResume;

        public bool TrySchedule(
            PartitionWorkItem item,
            out bool reachedHighWatermark)
        {
            reachedHighWatermark = false;
            if (Volatile.Read(ref _cancelled) != 0 || !_localPressure.TryAccept())
            {
                return false;
            }

            reachedHighWatermark = _localPressure.ShouldPause;
            var slot = KafkaPartitionKeySlotSelector.SelectSlot(
                item.ConsumeResult.Message?.Key,
                _channels.Length);
            if (_channels[slot].Writer.TryWrite(item))
            {
                return true;
            }

            _localPressure.OnCompleted();
            return false;
        }

        public void Cancel()
        {
            if (Interlocked.Exchange(ref _cancelled, 1) != 0)
            {
                return;
            }

            foreach (var channel in _channels)
            {
                channel.Writer.TryComplete();
            }

            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 并发完成时 CTS 可能已经释放；重复取消不改变最终状态。
            }
        }

        private async Task RunSlotAsync(Channel<PartitionWorkItem> channel)
        {
            try
            {
                await foreach (var item in channel.Reader.ReadAllAsync(_cancellation.Token))
                {
                    KafkaPartitionProcessingResult result;
                    var stopSlot = false;
                    try
                    {
                        _onHandlerStarted();
                        bool shouldCommit;
                        try
                        {
                            shouldCommit = await _processor(
                                    item.ConsumeResult,
                                    _cancellation.Token)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            _onHandlerStopped();
                        }

                        result = new KafkaPartitionProcessingResult(
                            item.ConsumeResult,
                            item.AssignmentEpoch,
                            shouldCommit,
                            null);
                        if (!shouldCommit)
                        {
                            stopSlot = true;
                            Cancel();
                        }
                    }
                    catch (Exception exception)
                    {
                        stopSlot = true;
                        result = new KafkaPartitionProcessingResult(
                            item.ConsumeResult,
                            item.AssignmentEpoch,
                            false,
                            exception);
                        Cancel();
                    }

                    ReleaseOne();
                    await _completionWriter.WriteAsync(result, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (stopSlot || _cancellation.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                // 当前在途消息会产生完成结果；尚未进入 Handler 的排队消息仅释放容量并等待重新投递。
            }
            finally
            {
                while (channel.Reader.TryRead(out _))
                {
                    ReleaseOne();
                }

                if (Interlocked.Decrement(ref _remainingSlots) == 0)
                {
                    _cancellation.Dispose();
                }
            }
        }

        private void ReleaseOne()
        {
            _localPressure.OnCompleted();
            _globalPressure.OnCompleted();
            _onStateChanged();
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

internal readonly record struct KafkaSchedulePressure(
    bool ReachedGlobalHighWatermark,
    bool ReachedPartitionHighWatermark);
