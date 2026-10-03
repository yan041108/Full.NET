namespace Full.NET.Logging.Kafka;

/// <summary>
/// 限制日志 Producer 已接收但尚未得到投递终态的消息数和估算驻留字节。
/// </summary>
/// <remarks>
/// 预约必须跨越 SDK 入队和投递回调，不能在 Produce 返回时释放；SDK 自身缓冲还须另设上限。
/// </remarks>
public sealed class KafkaLogProducerBudget
{
    /// <summary>单条记录的固定封套估算费用；实际 SDK/native 占用仍需容量测试。</summary>
    public const int EnvelopeOverheadBytes = 128;

    private readonly object _gate = new();
    private readonly int _maxMessages;
    private readonly long _maxBytes;
    private int _reservedMessages;
    private long _reservedBytes;

    /// <summary>建立后台 Producer 的双维度预算。</summary>
    /// <param name="maxMessages">允许等待投递终态的最大消息数。</param>
    /// <param name="maxBytes">允许等待投递终态的最大估算字节数。</param>
    public KafkaLogProducerBudget(int maxMessages, long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessages);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, EnvelopeOverheadBytes + 1L);
        _maxMessages = maxMessages;
        _maxBytes = maxBytes;
    }

    /// <summary>当前持有预约的消息数。</summary>
    public int ReservedMessages
    {
        get { lock (_gate) return _reservedMessages; }
    }

    /// <summary>当前持有的估算字节数。</summary>
    public long ReservedBytes
    {
        get { lock (_gate) return _reservedBytes; }
    }

    /// <summary>在构造 SDK 消息前非阻塞预约；容量不足时返回 false。</summary>
    /// <param name="payloadBytes">待发送 Compact JSON 的 UTF-8 字节数。</param>
    /// <param name="reservation">成功时返回必须在最终回调或失败路径释放的所有权令牌。</param>
    /// <returns>两个容量上限均允许本次消息时返回 true。</returns>
    public bool TryReserve(int payloadBytes, out Reservation? reservation)
        => TryReserve(payloadBytes, 0, out reservation);

    /// <summary>连同 Kafka key 的 UTF-8 字节数预约；用于带事件 ID 的实际发送。</summary>
    /// <param name="payloadBytes">JSON 载荷的 UTF-8 字节数。</param>
    /// <param name="keyBytes">Kafka key 的 UTF-8 字节数。</param>
    /// <param name="reservation">成功时必须在投递终态归还的令牌。</param>
    /// <returns>条数和估算字节预算都允许时返回 true。</returns>
    public bool TryReserve(int payloadBytes, int keyBytes, out Reservation? reservation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(keyBytes);
        var chargeBytes = (long)payloadBytes + keyBytes + EnvelopeOverheadBytes;
        lock (_gate)
        {
            if (_reservedMessages >= _maxMessages
                || chargeBytes > _maxBytes - _reservedBytes)
            {
                reservation = null;
                return false;
            }

            reservation = new Reservation(this, chargeBytes);
            _reservedMessages++;
            _reservedBytes += chargeBytes;
            return true;
        }
    }

    private void Release(long chargeBytes)
    {
        lock (_gate)
        {
            _reservedMessages--;
            _reservedBytes -= chargeBytes;
        }
    }

    /// <summary>代表一条消息从预约到最终投递结果之间的容量所有权。</summary>
    public sealed class Reservation : IDisposable
    {
        private KafkaLogProducerBudget? _owner;
        private readonly long _chargeBytes;

        internal Reservation(KafkaLogProducerBudget owner, long chargeBytes)
        {
            _owner = owner;
            _chargeBytes = chargeBytes;
        }

        /// <summary>恰好归还一次消息数与字节容量，可在投递回调和失败清理竞态下重复调用。</summary>
        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release(_chargeBytes);
        }
    }
}
