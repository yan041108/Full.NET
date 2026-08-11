using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 表示开放环调度产生的一个绝对到达槽位。
/// </summary>
public sealed record KafkaCapacityScheduledMessage(
    long GlobalSequence,
    long ScheduledTimestampMicroseconds);

/// <summary>
/// 表示开放环调度的有界结果和保护性停止原因。
/// </summary>
public sealed record KafkaCapacitySchedulingResult(
    long Scheduled,
    long Missed,
    int ChannelCapacity,
    int MaximumBufferedMessages,
    string? StopReasonCode);

internal interface IKafkaCapacityClock
{
    long GetTimestampMicroseconds();

    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>
/// 以绝对单调时间运行有界开放环调度，避免完成驱动和无界追赶失真。
/// </summary>
public sealed class KafkaCapacityOpenLoopScheduler
{
    private const long MicrosecondsPerSecond = 1_000_000;
    private readonly IKafkaCapacityClock clock;

    public KafkaCapacityOpenLoopScheduler()
        : this(StopwatchKafkaCapacityClock.Instance)
    {
    }

    internal KafkaCapacityOpenLoopScheduler(IKafkaCapacityClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<KafkaCapacitySchedulingResult> RunAsync(
        int targetMessagesPerSecond,
        TimeSpan duration,
        int maximumMessages,
        int producerConcurrency,
        Func<KafkaCapacityScheduledMessage, CancellationToken, ValueTask> writeAsync,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetMessagesPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMessages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(producerConcurrency);
        ArgumentNullException.ThrowIfNull(writeAsync);
        var durationMicroseconds = checked((long)Math.Ceiling(
            duration.TotalMilliseconds * 1_000d));
        if (durationMicroseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var channelCapacity = Math.Min(
            maximumMessages,
            Math.Min(
                1_000_000,
                Math.Max(1_024, checked(producerConcurrency * 4_096))));
        var channel = Channel.CreateBounded<KafkaCapacityScheduledMessage>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = producerConcurrency == 1,
                AllowSynchronousContinuations = false,
            });
        using var executionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var workers = Enumerable.Range(0, producerConcurrency)
            .Select(_ => ConsumeAsync(
                channel.Reader,
                writeAsync,
                executionCancellation))
            .ToArray();

        Exception? schedulingFailure = null;
        KafkaCapacitySchedulingResult? result = null;
        try
        {
            result = await ScheduleAsync(
                channel,
                targetMessagesPerSecond,
                durationMicroseconds,
                maximumMessages,
                channelCapacity,
                executionCancellation.Token);
        }
        catch (Exception exception)
        {
            schedulingFailure = exception;
            executionCancellation.Cancel();
        }
        finally
        {
            channel.Writer.TryComplete(schedulingFailure);
        }

        Exception? workerFailure = null;
        try
        {
            await Task.WhenAll(workers);
        }
        catch (Exception exception)
        {
            workerFailure = exception;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (workerFailure is not null)
        {
            ExceptionDispatchInfo.Capture(workerFailure).Throw();
        }

        if (schedulingFailure is not null)
        {
            ExceptionDispatchInfo.Capture(schedulingFailure).Throw();
        }

        return result!;
    }

    private async Task<KafkaCapacitySchedulingResult> ScheduleAsync(
        Channel<KafkaCapacityScheduledMessage> channel,
        int targetMessagesPerSecond,
        long durationMicroseconds,
        int maximumMessages,
        int channelCapacity,
        CancellationToken cancellationToken)
    {
        var start = clock.GetTimestampMicroseconds();
        var end = checked(start + durationMicroseconds);
        long slot = 0;
        long scheduled = 0;
        long missed = 0;
        var maximumBuffered = 0;
        long currentWindow = 0;
        long currentWindowScheduled = 0;
        var consecutiveUnderTargetWindows = 0;
        string? stopReasonCode = null;

        while (scheduled < maximumMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deadline = checked(
                start + (slot * MicrosecondsPerSecond / targetMessagesPerSecond));
            if (deadline >= end)
            {
                break;
            }

            var now = clock.GetTimestampMicroseconds();
            if (now < deadline)
            {
                await clock.DelayAsync(
                    TimeSpan.FromTicks((deadline - now) * 10),
                    cancellationToken);
                now = clock.GetTimestampMicroseconds();
            }

            if (now >= end)
            {
                var totalSlots = Math.Min(
                    (long)maximumMessages,
                    checked((durationMicroseconds * targetMessagesPerSecond
                        + MicrosecondsPerSecond - 1) / MicrosecondsPerSecond));
                missed += Math.Max(0, totalSlots - slot);
                break;
            }

            if (now > deadline)
            {
                var currentSlot = Math.Min(
                    checked((now - start) * targetMessagesPerSecond
                        / MicrosecondsPerSecond),
                    checked((durationMicroseconds * targetMessagesPerSecond - 1)
                        / MicrosecondsPerSecond));
                if (currentSlot > slot)
                {
                    missed += currentSlot - slot;
                    slot = currentSlot;
                    deadline = checked(
                        start + (slot * MicrosecondsPerSecond
                            / targetMessagesPerSecond));
                }
            }

            if (deadline >= end)
            {
                break;
            }

            await channel.Writer.WriteAsync(
                new KafkaCapacityScheduledMessage(scheduled, deadline),
                cancellationToken);
            scheduled++;
            slot++;
            UpdateMaximum(
                ref maximumBuffered,
                channel.Reader.CanCount ? channel.Reader.Count : 0);

            now = clock.GetTimestampMicroseconds();
            var completedWindows = Math.Max(0, (now - start) / MicrosecondsPerSecond);
            while (currentWindow < completedWindows)
            {
                if (currentWindowScheduled
                    < targetMessagesPerSecond * 0.95d)
                {
                    consecutiveUnderTargetWindows++;
                }
                else
                {
                    consecutiveUnderTargetWindows = 0;
                }

                currentWindow++;
                currentWindowScheduled = 0;
            }

            currentWindowScheduled++;
            if (consecutiveUnderTargetWindows >= 10)
            {
                stopReasonCode = "scheduling_rate_below_95_percent";
                break;
            }
        }

        return new KafkaCapacitySchedulingResult(
            scheduled,
            missed,
            channelCapacity,
            maximumBuffered,
            stopReasonCode);
    }

    private static async Task ConsumeAsync(
        ChannelReader<KafkaCapacityScheduledMessage> reader,
        Func<KafkaCapacityScheduledMessage, CancellationToken, ValueTask> writeAsync,
        CancellationTokenSource executionCancellation)
    {
        try
        {
            await foreach (var message in reader.ReadAllAsync(
                               executionCancellation.Token))
            {
                await writeAsync(message, executionCancellation.Token);
            }
        }
        catch
        {
            executionCancellation.Cancel();
            throw;
        }
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref maximum);
            if (candidate <= current
                || Interlocked.CompareExchange(ref maximum, candidate, current) == current)
            {
                return;
            }
        }
    }

    private sealed class StopwatchKafkaCapacityClock : IKafkaCapacityClock
    {
        public static readonly StopwatchKafkaCapacityClock Instance = new();

        public long GetTimestampMicroseconds() =>
            Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()).Ticks / 10;

        public async ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            await Task.Delay(delay, TimeProvider.System, cancellationToken);
    }
}
