using System.Text.Json;
using System.Globalization;

namespace Full.NET.LogConsumer;

/// <summary>日志 Kafka 消费输入的低基数校验结果；失败项不得交给 ES 或标记完成。</summary>
public enum LogRecordValidationResult
{
    /// <summary>已通过当前 Compact JSON 必需字段校验。</summary>
    Valid,

    /// <summary>Kafka 记录缺少来源事件键。</summary>
    MissingKey,

    /// <summary>记录超过该消费者的单事件字节预算。</summary>
    Oversize,

    /// <summary>载荷不是有效的有界 JSON。</summary>
    InvalidJson,

    /// <summary>必需字段缺失、重复或类型不符。</summary>
    InvalidEnvelope,

    /// <summary>Kafka 键与 JSON 内的规范事件 ID 不一致。</summary>
    IdMismatch,

    /// <summary>事件时间缺失或不是有效的 ISO 时间。</summary>
    InvalidTimestamp,

    /// <summary>日志分类不在已审定的固定集合中。</summary>
    InvalidClass,

    /// <summary>冻结的索引路由字段缺失、重复、矛盾或超出允许范围。</summary>
    InvalidRoute,
}

/// <summary>拥有独立载荷副本的已解析 Compact JSON 记录；尚未取得脱敏或索引路由资格。</summary>
public sealed class ParsedLogRecord
{
    private readonly byte[] _utf8Json;

    internal ParsedLogRecord(
        string logEventId,
        DateTimeOffset occurredAtUtc,
        string logClass,
        int indexRouteVersion,
        DateTimeOffset expiresAtUtc,
        byte[] utf8Json)
    {
        LogEventId = logEventId;
        OccurredAtUtc = occurredAtUtc;
        LogClass = logClass;
        IndexRouteVersion = indexRouteVersion;
        ExpiresAtUtc = expiresAtUtc;
        _utf8Json = utf8Json;
    }

    /// <summary>与 Kafka key 一致的规范事件 ID。</summary>
    public string LogEventId { get; }

    /// <summary>由原始 @t 冻结的 UTC 事件时间，不使用消费或重试时间。</summary>
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>只允许固定的 diagnostic、http.operation 或 security 分类。</summary>
    public string LogClass { get; }

    /// <summary>随事件冻结的受控索引路由版本；写入前仍须与消费者版本策略核对。</summary>
    public int IndexRouteVersion { get; }

    /// <summary>随事件冻结的绝对 UTC 到期时间，重试不能延长。</summary>
    public DateTimeOffset ExpiresAtUtc { get; }

    /// <summary>校验后复制并独立持有的原始 UTF-8 Compact JSON；调用方若需跨异步边界持有，应自行复制。</summary>
    public ReadOnlySpan<byte> Utf8Json => _utf8Json;

    /// <summary>仅对版本保留期精确匹配且尚未过期的事件构造固定 UTC 日期索引名。</summary>
    /// <param name="retentionDaysByVersion">部署时批准的路由版本与固定保留天数映射。</param>
    /// <param name="utcNow">写入判定时刻；必须来自受信任时钟。</param>
    /// <param name="indexName">成功时返回受控索引名，否则为 null。</param>
    /// <returns>事件仍有写入资格时为 true。</returns>
    public bool TryGetIndexName(
        IReadOnlyDictionary<int, int> retentionDaysByVersion,
        DateTimeOffset utcNow,
        out string? indexName)
    {
        ArgumentNullException.ThrowIfNull(retentionDaysByVersion);
        indexName = null;
        if (!retentionDaysByVersion.TryGetValue(IndexRouteVersion, out var retentionDays)
            || retentionDays is < 1 or > 3650
            || OccurredAtUtc > DateTimeOffset.MaxValue.AddDays(-retentionDays)
            || ExpiresAtUtc != OccurredAtUtc.AddDays(retentionDays)
            || utcNow.ToUniversalTime() >= ExpiresAtUtc)
        {
            return false;
        }

        indexName = string.Create(CultureInfo.InvariantCulture,
            $"fn-logs-{IndexRouteVersion}-{LogClass}-{OccurredAtUtc:yyyy.MM.dd}");
        return true;
    }
}

/// <summary>在进入 ES/DLQ 路由前校验现有日志 Kafka 线格式与来源键。</summary>
public static class KafkaLogRecordParser
{
    /// <summary>仅校验现有输入字段；不能把返回记录直接写入 ES 或解释为完整隐私校验。</summary>
    /// <param name="kafkaKey">日志 Producer 使用的来源事件 ID。</param>
    /// <param name="payload">Kafka 值的 UTF-8 Compact JSON。</param>
    /// <param name="maxEventBytes">消费者接受的单事件最大 UTF-8 字节数。</param>
    /// <param name="record">成功时返回独立持有载荷的记录，失败时为 null。</param>
    /// <returns>低基数校验结果，不包含原始载荷或异常消息。</returns>
    public static LogRecordValidationResult TryParse(
        string? kafkaKey,
        ReadOnlyMemory<byte> payload,
        int maxEventBytes,
        out ParsedLogRecord? record)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEventBytes);
        record = null;
        if (string.IsNullOrEmpty(kafkaKey))
        {
            return LogRecordValidationResult.MissingKey;
        }

        if (payload.Length > maxEventBytes)
        {
            return LogRecordValidationResult.Oversize;
        }

        // Serilog Compact 快照自带尾随换行；Bulk 只禁止 JSON 内部跨行。
        var singleLine = payload.Span;
        while (!singleLine.IsEmpty && singleLine[^1] is (byte)'\r' or (byte)'\n')
        {
            singleLine = singleLine[..^1];
        }
        if (singleLine.IndexOfAny((byte)'\r', (byte)'\n') >= 0)
        {
            return LogRecordValidationResult.InvalidEnvelope;
        }

        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return LogRecordValidationResult.InvalidEnvelope;
            }

            JsonElement timestamp = default;
            JsonElement template = default;
            JsonElement eventId = default;
            JsonElement classification = default;
            JsonElement frozenOccurredAt = default;
            JsonElement expiresAt = default;
            JsonElement routeVersion = default;
            var seen = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var bit = property.Name switch
                {
                    "@t" => 1,
                    "@mt" => 2,
                    "LogEventId" => 4,
                    "log.class" => 8,
                    "OccurredAtUtc" => 16,
                    "ExpiresAtUtc" => 32,
                    "IndexRouteVersion" => 64,
                    _ => 0,
                };
                if (bit == 0)
                {
                    continue;
                }

                if ((seen & bit) != 0)
                {
                    return LogRecordValidationResult.InvalidEnvelope;
                }

                seen |= bit;
                switch (bit)
                {
                    case 1: timestamp = property.Value; break;
                    case 2: template = property.Value; break;
                    case 4: eventId = property.Value; break;
                    case 8: classification = property.Value; break;
                    case 16: frozenOccurredAt = property.Value; break;
                    case 32: expiresAt = property.Value; break;
                    case 64: routeVersion = property.Value; break;
                }
            }

            if ((seen & 6) != 6
                || template.ValueKind != JsonValueKind.String
                || eventId.ValueKind != JsonValueKind.String
                || !TryCanonicalGuid(eventId.GetString(), out var id))
            {
                return LogRecordValidationResult.InvalidEnvelope;
            }

            if (!string.Equals(kafkaKey, id, StringComparison.Ordinal))
            {
                return LogRecordValidationResult.IdMismatch;
            }

            if ((seen & 1) == 0
                || timestamp.ValueKind != JsonValueKind.String
                || !HasExplicitUtcOffset(timestamp.GetString())
                || !timestamp.TryGetDateTimeOffset(out var occurredAt))
            {
                return LogRecordValidationResult.InvalidTimestamp;
            }

            if ((seen & 8) == 0
                || classification.ValueKind != JsonValueKind.String
                || classification.GetString() is not ("diagnostic" or "http.operation" or "security"))
            {
                return LogRecordValidationResult.InvalidClass;
            }

            if ((seen & 112) != 112
                || frozenOccurredAt.ValueKind != JsonValueKind.String
                || expiresAt.ValueKind != JsonValueKind.String
                || routeVersion.ValueKind != JsonValueKind.Number
                || !frozenOccurredAt.TryGetDateTimeOffset(out var frozenTime)
                || !expiresAt.TryGetDateTimeOffset(out var expiry)
                || frozenTime.Offset != TimeSpan.Zero
                || expiry.Offset != TimeSpan.Zero
                || frozenTime != occurredAt.ToUniversalTime()
                || expiry <= frozenTime
                || expiry - frozenTime > TimeSpan.FromDays(3650)
                || !routeVersion.TryGetInt32(out var version)
                || version is < 1 or > 9999)
            {
                return LogRecordValidationResult.InvalidRoute;
            }

            record = new ParsedLogRecord(
                id,
                occurredAt.ToUniversalTime(),
                classification.GetString()!,
                version,
                expiry,
                payload.ToArray());
            return LogRecordValidationResult.Valid;
        }
        catch (JsonException)
        {
            return LogRecordValidationResult.InvalidJson;
        }
    }

    private static bool TryCanonicalGuid(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (value is null
            || value.Length != 36
            || !Guid.TryParseExact(value, "D", out var parsed))
        {
            return false;
        }

        canonical = parsed.ToString("D");
        return string.Equals(value, canonical, StringComparison.Ordinal);
    }

    private static bool HasExplicitUtcOffset(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value[^1] == 'Z')
        {
            return true;
        }

        return value.Length >= 6
            && value[^6] is '+' or '-'
            && char.IsAsciiDigit(value[^5])
            && char.IsAsciiDigit(value[^4])
            && value[^3] == ':'
            && char.IsAsciiDigit(value[^2])
            && char.IsAsciiDigit(value[^1]);
    }
}
