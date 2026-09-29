using Serilog.Events;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 持有自身 UTF-8 缓冲；旧 Sink 启用时可附带不含原始业务对象的受限事件拷贝。
/// </summary>
internal sealed class LogEnvelope(
    byte[] utf8Json,
    LogEvent? legacyEvent = null,
    int legacyEventChargeBytes = 0,
    string? logEventId = null)
{
    public const int ChargeOverheadBytes = 128;
    private const int LegacyMaximumFixedChargeBytes = 65_536;

    public ReadOnlyMemory<byte> Utf8Json => utf8Json;

    public LogEvent? LegacyEvent => legacyEvent;

    public string? LogEventId => logEventId;

    // 附带受限事件时，按其有限对象图另收保守费用。
    public int ChargeBytes => checked(utf8Json.Length + ChargeOverheadBytes + legacyEventChargeBytes);

    public static int MaxChargeBytes(int maxEventBytes, bool retainLegacyEvent) =>
        checked(maxEventBytes + ChargeOverheadBytes
            + (retainLegacyEvent ? LegacyMaximumFixedChargeBytes + maxEventBytes * 4 : 0));

    public static int MaxLegacyEventChargeBytes(int maxEventBytes) =>
        checked(LegacyMaximumFixedChargeBytes + maxEventBytes * 4);
}
