namespace Full.NET.Hosting.Observability;

/// <summary>仅持有自身 UTF-8 安全快照；后台不保留 Serilog 事件或原始业务对象图。</summary>
internal sealed class LogEnvelope(byte[] utf8Json, string? logEventId = null)
{
    /// <summary>快照排队的单调时间，只用于队列年龄观测。</summary>
    public long QueuedAtTimestamp { get; } = System.Diagnostics.Stopwatch.GetTimestamp();
    public const int ChargeOverheadBytes = 128;
    public ReadOnlyMemory<byte> Utf8Json => utf8Json;
    public string? LogEventId => logEventId;
    public int ChargeBytes => checked(utf8Json.Length + ChargeOverheadBytes);
    public static int MaxChargeBytes(int maxEventBytes) => checked(maxEventBytes + ChargeOverheadBytes);
}
