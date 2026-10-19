using System.Text.Json;
using Full.NET.Abstractions.Observability;

namespace Full.NET.Modules.ObservabilityAdmin.Features.QueryLogTimeline;

/// <summary>只投影固定关联字段；不把磁盘/ES 的任意正文反射到管理端。</summary>
internal static class RuntimeLogTimelineParser
{
    internal static LogTimelineEntry? Parse(JsonElement root, string source, LogTimelineQuery query)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        string? Text(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: <= 128 } text ? text : null;
        Guid? Id(string key) => Guid.TryParse(Text(key), out var id) && id != Guid.Empty ? id : null;
        if (!Guid.TryParse(Text("LogEventId"), out var eventId)
            || !DateTimeOffset.TryParse(Text("@t") ?? Text("OccurredAtUtc"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var time)
            || time < query.FromUtc || time >= query.ToUtc) return null;
        var correlation = new LogCorrelationSnapshot
        {
            TraceId = Hex(Text("TraceId") ?? Text("@tr"), 32),
            SpanId = Hex(Text("SpanId") ?? Text("@sp"), 16),
            ParentSpanId = Hex(Text("ParentSpanId"), 16), RequestId = Text("RequestId"),
            UserId = Id("UserId"), TenantId = Id("TenantId"), SessionId = Id("SessionId"),
            CenterSessionId = Id("CenterSessionId"), ApplicationSessionId = Id("ApplicationSessionId"),
            ClientIpFingerprint = Hex(Text("ClientIpFingerprint"), 64),
        };
        if (query.TraceId is not null && query.TraceId != correlation.TraceId
            || query.UserId is not null && query.UserId != correlation.UserId
            || query.SessionId is not null && query.SessionId != correlation.SessionId
                && query.SessionId != correlation.CenterSessionId && query.SessionId != correlation.ApplicationSessionId
            || query.ClientIpFingerprint is not null && query.ClientIpFingerprint != correlation.ClientIpFingerprint) return null;
        var category = Text("log.class") is "http.operation" ? "http.operation" : "diagnostic";
        return new(eventId.ToString("D"), source, time.ToUniversalTime(), category, correlation);
    }

    private static string? Hex(string? text, int length) => text?.Length == length && text.All(char.IsAsciiHexDigit)
        ? text.ToLowerInvariant() : null;
}
