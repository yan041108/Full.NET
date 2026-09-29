namespace Full.NET.Modules.Auditing.Features.WriteAuditBatch;

/// <summary>并发 B1 信封的计费字节预留；调用方负责在终态释放一次。</summary>
internal sealed class AuditQueueByteBudget
{
    private long _reservedBytes;

    public long ReservedBytes => Volatile.Read(ref _reservedBytes);

    public bool TryReserve(int bytes, long capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);

        while (true)
        {
            var current = Volatile.Read(ref _reservedBytes);
            if (bytes > capacityBytes - current)
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
                    "The audit byte reservation was already released or exceeds the reserved total.");
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
