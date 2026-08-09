namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 用高低水位维护有界 Buffer 深度；高水位停止接收，低水位才恢复，避免临界值附近反复 Pause/Resume。
/// </summary>
internal sealed class KafkaConsumerBufferPressure
{
    private readonly int _highWatermark;
    private readonly int _lowWatermark;
    private int _depth;

    public KafkaConsumerBufferPressure(int highWatermark, int lowWatermark)
    {
        if (highWatermark < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(highWatermark));
        }

        if (lowWatermark < 0 || lowWatermark >= highWatermark)
        {
            throw new ArgumentOutOfRangeException(nameof(lowWatermark));
        }

        _highWatermark = highWatermark;
        _lowWatermark = lowWatermark;
    }

    public int Depth => Volatile.Read(ref _depth);

    public bool ShouldPause => Depth >= _highWatermark;

    public bool ShouldResume => Depth <= _lowWatermark;

    public bool TryAccept()
    {
        while (true)
        {
            var current = Volatile.Read(ref _depth);
            if (current >= _highWatermark)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _depth, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    public void OnCompleted(int count = 1)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        while (true)
        {
            var current = Volatile.Read(ref _depth);
            if (current < count)
            {
                throw new InvalidOperationException(
                    "Kafka consumer buffer depth cannot be reduced below zero.");
            }

            if (Interlocked.CompareExchange(ref _depth, current - count, current) == current)
            {
                return;
            }
        }
    }
}
