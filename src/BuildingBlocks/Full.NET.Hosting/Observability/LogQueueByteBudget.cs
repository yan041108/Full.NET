namespace Full.NET.Hosting.Observability;

/// <summary>
/// 原子限制一条日志通道已预留的字节数；消费端持有 Sink 缓冲期间必须继续计费。
/// </summary>
/// <remarks>
/// 成功预留的调用方必须在封套终态恰好释放一次相同字节数；队列出列不等于 Sink 已消费完成。
/// </remarks>
internal sealed class LogQueueByteBudget
{
    private readonly long _capacityBytes;
    private long _reservedBytes;

    public LogQueueByteBudget(long capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);
        _capacityBytes = capacityBytes;
    }

    public long CapacityBytes => _capacityBytes;

    public long ReservedBytes => Volatile.Read(ref _reservedBytes);

    public bool TryReserve(int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        while (true)
        {
            var current = Volatile.Read(ref _reservedBytes);
            if (bytes > _capacityBytes - current)
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref _reservedBytes,
                    current + bytes,
                    current) == current)
            {
                return true;
            }
        }
    }

    public void Release(int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        while (true)
        {
            var current = Volatile.Read(ref _reservedBytes);
            if (bytes > current)
            {
                throw new InvalidOperationException(
                    "The log byte reservation was already released or exceeds the reserved total.");
            }

            if (Interlocked.CompareExchange(
                    ref _reservedBytes,
                    current - bytes,
                    current) == current)
            {
                return;
            }
        }
    }
}
