using System.Text;
using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Parsing;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 在入队前把 Serilog 事件限制为可独立持有的 Compact JSON；超限先移除诊断属性。
/// </summary>
internal static class LogEnvelopeBuilder
{
    private const int MaxStringChars = 2048;
    private const int MaxProperties = 64;
    private const int MaxScannedProperties = 256;
    private const int MaxNodes = 256;
    private const int MaxDepth = 4;
    private const int MaxCollectionItems = 16;
    private const string Truncated = "[truncated]";
    private const string UnsupportedScalar = "[unsupported scalar]";
    private static readonly MessageTemplateParser Parser = new();
    private static readonly CompactJsonFormatter Formatter = new();
    private static readonly HashSet<string> EssentialProperties = new(StringComparer.Ordinal)
    {
        "LogEventId", "Instance", "Application", "log.class", "reliability.class",
        "log.stream", "http.method", "http.route", "http.status_code",
        "TraceId", "SpanId", "RequestId", "Outcome", "StatusCode", "ExceptionType",
        "SchemaVersion", "OccurredAtUtc", "ExpiresAtUtc", "IndexRouteVersion",
        "DataClassification", "EventId", "EventName", "SourceContext", "TenantId",
    };
    private static readonly string[] RestrictedMetadataKeys =
    [
        "LogEventId", "log.class", "log.stream", "reliability.class",
        "TraceId", "SpanId", "http.status_code", "StatusCode",
    ];
    private static readonly HashSet<string> HttpOperationOptionalProperties = new(StringComparer.Ordinal)
    {
        "data.classification", "DiagnosticGroup", "url", "ElapsedMs",
        "SourceOriginFingerprint", "ClientIpFingerprint", "EndpointName",
        "EndpointDisplayName", "Controller", "Action", "Area", "Scheme",
        "HttpProtocol", "UserAgentSummary", "Culture", "AcceptLanguageSummary",
        "ClientIdFingerprint", "CaptureThreadId", "RequestPayload", "ResponsePayload",
        "HttpMethod", "Route",
    };
    private static readonly HashSet<string> HttpOperationReservedProperties = new(StringComparer.Ordinal)
    {
        "http.method", "http.route", "http.status_code", "StatusCode", "Outcome",
        "log.stream", "DiagnosticGroup", "url", "HttpMethod", "Route",
        "SourceOriginFingerprint", "ClientIpFingerprint", "EndpointName",
        "EndpointDisplayName", "Controller", "Action", "Area", "Scheme",
        "HttpProtocol", "UserAgentSummary", "Culture", "AcceptLanguageSummary",
        "ClientIdFingerprint", "CaptureThreadId", "RequestPayload", "ResponsePayload",
    };

    public static bool TryBuild(
        LogEvent source,
        int maxEventBytes,
        out LogEnvelope? envelope,
        bool retainLegacyEvent = false,
        HttpOperationLogRecord? trustedHttp = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEventBytes);

        source = trustedHttp is null
            ? ResolveHttpOperationSource(source)
            : CreateTrustedHttpSource(source, trustedHttp);
        var safeEvent = CreateSafeEvent(source, essentialsOnly: false);
        if (TryFormat(safeEvent, maxEventBytes, retainLegacyEvent, out envelope))
        {
            return true;
        }

        // 大型诊断属性先整体移除；关联键和结果仍超限时才拒绝事件。
        safeEvent = CreateSafeEvent(source, essentialsOnly: true);
        return TryFormat(safeEvent, maxEventBytes, retainLegacyEvent, out envelope);
    }

    private static LogEvent ResolveHttpOperationSource(LogEvent source)
    {
        var isHttpOperation = source.Properties.TryGetValue("log.class", out var classification)
            && classification is ScalarValue { Value: LogClassification.HttpOperation };
        if (!isHttpOperation)
        {
            return source;
        }

        var properties = new List<LogEventProperty>();
        properties.Add(new LogEventProperty("log.class", new ScalarValue(LogClassification.Diagnostic)));

        // 管道生成的标识与资源字段保留；调用方的模板、异常和其他属性一律不进入 HTTP 摘要。
        foreach (var key in new[] { "LogEventId", "Instance", "Application" })
        {
            if (source.Properties.TryGetValue(key, out var value))
            {
                properties.Add(new LogEventProperty(key, value));
            }
        }

        return new LogEvent(
            source.Timestamp,
            source.Level,
            null,
            Parser.Parse("[untrusted http operation classification]"),
            properties);
    }

    private static LogEvent CreateTrustedHttpSource(LogEvent source, HttpOperationLogRecord record)
    {
        var properties = new List<LogEventProperty>(record.Fields.Count + 3);
        foreach (var field in record.Fields)
        {
            properties.Add(new LogEventProperty(field.Key, new ScalarValue(field.Value)));
        }

        foreach (var key in new[] { "LogEventId", "Instance", "Application" })
        {
            if (source.Properties.TryGetValue(key, out var value))
            {
                properties.Add(new LogEventProperty(key, value));
            }
        }

        return new LogEvent(
            source.Timestamp,
            source.Level,
            null,
            Parser.Parse("HttpOperationCompleted"),
            properties);
    }

    private static LogEvent CreateSafeEvent(LogEvent source, bool essentialsOnly)
    {
        var template = source.MessageTemplate.Text;
        var restrictedTemplate = template.Length > MaxStringChars;
        if (restrictedTemplate)
        {
            template = "[message template omitted]";
        }
        else if (ContainsSensitiveText(template))
        {
            template = HttpOperationLogSanitizer.Redacted;
            restrictedTemplate = true;
        }

        var parsedTemplate = Parser.Parse(template);
        if (parsedTemplate.Tokens.Count() > 64)
        {
            parsedTemplate = Parser.Parse("[message template omitted]");
            restrictedTemplate = true;
        }

        var remainingNodes = MaxNodes;
        var properties = new List<LogEventProperty>(Math.Min(source.Properties.Count, MaxProperties));
        var propertyLimit = MaxProperties - (source.Exception is null ? 0 : 1);
        if (restrictedTemplate)
        {
            // 模板声明了凭据语境时，通用占位符即使不叫 Token 也可能是原值。
            // 路由与关联字段只接受固定值或严格格式，不能把任意字符串带进输出。
            AddRestrictedMetadata(source, properties);
        }
        else
        {
            AddEssentialProperties(source, properties, propertyLimit, ref remainingNodes);
            if (!essentialsOnly)
            {
                AddOptionalProperties(source, properties, propertyLimit, ref remainingNodes);
            }
        }

        if (source.Exception is not null)
        {
            var name = source.Exception.GetType().Name;
            properties.Add(new LogEventProperty(
                "ExceptionType",
                new ScalarValue(BoundString(name, 128))));
        }

        var safeException = source.Exception is null
            ? null
            : new LogSnapshotException(BoundString(source.Exception.GetType().Name, 128));
        return source.TraceId is { } traceId && source.SpanId is { } spanId
            ? new LogEvent(
                source.Timestamp,
                source.Level,
                safeException,
                parsedTemplate,
                properties,
                traceId,
                spanId)
            : new LogEvent(
                source.Timestamp,
                source.Level,
                safeException,
                parsedTemplate,
                properties);
    }

    private static void AddRestrictedMetadata(LogEvent source, List<LogEventProperty> properties)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var tokenCount = 0;
        foreach (var token in source.MessageTemplate.Tokens)
        {
            if (++tokenCount > 64)
            {
                break;
            }

            if (token is PropertyToken property)
            {
                referenced.Add(property.PropertyName);
            }
        }

        foreach (var key in RestrictedMetadataKeys)
        {
            if ((key == "LogEventId" || !referenced.Contains(key))
                && source.Properties.TryGetValue(key, out var value)
                && value is ScalarValue scalar
                && IsValidatedMetadata(key, scalar.Value))
            {
                properties.Add(new LogEventProperty(key, new ScalarValue(scalar.Value)));
            }
        }
    }

    private static bool IsValidatedMetadata(string key, object? value) => key switch
    {
        "LogEventId" => value is string eventId
            && eventId.Length == 36
            && Guid.TryParseExact(eventId, "D", out _),
        "log.class" => value is LogClassification.HttpOperation
            or LogClassification.Diagnostic or LogClassification.Security,
        "log.stream" => value is HttpOperationLogMiddleware.LogStream,
        "reliability.class" => value is "Priority" or "BestEffort",
        "TraceId" => value is string traceId && IsHexId(traceId, 32),
        "SpanId" => value is string spanId && IsHexId(spanId, 16),
        "http.status_code" or "StatusCode" => value is int statusCode
            && statusCode is >= 100 and <= 599,
        _ => false,
    };

    private static bool IsHexId(string value, int length)
    {
        if (value.Length != length)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddEssentialProperties(
        LogEvent source,
        List<LogEventProperty> properties,
        int propertyLimit,
        ref int remainingNodes)
    {
        var isHttpOperation = source.Properties.TryGetValue("log.class", out var classification)
            && classification is ScalarValue { Value: LogClassification.HttpOperation };
        foreach (var key in EssentialProperties)
        {
            if (properties.Count == propertyLimit)
            {
                break;
            }

            if (key == "ExceptionType" && source.Exception is not null
                || !isHttpOperation && HttpOperationReservedProperties.Contains(key)
                || !source.Properties.TryGetValue(key, out var value))
            {
                continue;
            }

            properties.Add(new LogEventProperty(
                key,
                HttpOperationLogSanitizer.IsSensitiveKey(key)
                    ? new ScalarValue(HttpOperationLogSanitizer.Redacted)
                    : CopyValue(value, 0, ref remainingNodes)));
        }
    }

    private static void AddOptionalProperties(
        LogEvent source,
        List<LogEventProperty> properties,
        int propertyLimit,
        ref int remainingNodes)
    {
        var scanned = 0;
        var isHttpOperation = source.Properties.TryGetValue("log.class", out var classification)
            && classification is ScalarValue { Value: LogClassification.HttpOperation };
        foreach (var property in source.Properties)
        {
            if (properties.Count == propertyLimit || scanned++ == MaxScannedProperties)
            {
                break;
            }

            if (property.Key.Length > 128
                || ContainsSensitiveText(property.Key)
                || EssentialProperties.Contains(property.Key)
                || !isHttpOperation && HttpOperationReservedProperties.Contains(property.Key)
                || IsRestrictedDetailKey(property.Key)
                    && !IsApprovedPayload(property.Key, property.Value, isHttpOperation)
                || isHttpOperation && !HttpOperationOptionalProperties.Contains(property.Key))
            {
                continue;
            }

            properties.Add(new LogEventProperty(
                property.Key,
                HttpOperationLogSanitizer.IsSensitiveKey(property.Key)
                    ? new ScalarValue(HttpOperationLogSanitizer.Redacted)
                    : CopyValue(property.Value, 0, ref remainingNodes)));
        }
    }

    private static LogEventPropertyValue CopyValue(
        LogEventPropertyValue value,
        int depth,
        ref int remainingNodes)
    {
        if (remainingNodes-- <= 0 || depth >= MaxDepth)
        {
            return new ScalarValue(Truncated);
        }

        switch (value)
        {
            case ScalarValue scalar:
                return CopyScalar(scalar);
            case SequenceValue sequence:
            {
                var items = new List<LogEventPropertyValue>(MaxCollectionItems);
                foreach (var item in sequence.Elements)
                {
                    if (items.Count == MaxCollectionItems)
                    {
                        break;
                    }

                    items.Add(CopyValue(item, depth + 1, ref remainingNodes));
                }

                return new SequenceValue(items);
            }
            case StructureValue structure:
            {
                var members = new List<LogEventProperty>(MaxCollectionItems);
                foreach (var member in structure.Properties)
                {
                    if (members.Count == MaxCollectionItems)
                    {
                        break;
                    }

                    if (member.Name.Length <= 128
                        && !ContainsSensitiveText(member.Name)
                        && !IsRestrictedDetailKey(member.Name))
                    {
                        members.Add(new LogEventProperty(
                            member.Name,
                            HttpOperationLogSanitizer.IsSensitiveKey(member.Name)
                                ? new ScalarValue(HttpOperationLogSanitizer.Redacted)
                                : CopyValue(member.Value, depth + 1, ref remainingNodes)));
                    }
                }

                return new StructureValue(
                    members,
                    structure.TypeTag is null
                        ? null
                        : SanitizeText(BoundString(structure.TypeTag, 128)));
            }
            case DictionaryValue dictionary:
            {
                var entries = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(
                    MaxCollectionItems);
                foreach (var entry in dictionary.Elements)
                {
                    if (entries.Count == MaxCollectionItems)
                    {
                        break;
                    }

                    entries.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                        CopyScalar(entry.Key),
                        entry.Key.Value is string key
                            && (HttpOperationLogSanitizer.IsSensitiveKey(key)
                                || IsRestrictedDetailKey(key))
                                ? new ScalarValue(HttpOperationLogSanitizer.Redacted)
                                : CopyValue(entry.Value, depth + 1, ref remainingNodes)));
                }

                return new DictionaryValue(entries);
            }
            default:
                return new ScalarValue(UnsupportedScalar);
        }
    }

    private static ScalarValue CopyScalar(ScalarValue scalar)
    {
        var value = scalar.Value;
        return value switch
        {
            null => new ScalarValue(null),
            string text => new ScalarValue(SanitizeText(BoundString(text, MaxStringChars))),
            char character => new ScalarValue(character.ToString()),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal or Guid or DateTime or DateTimeOffset
                or TimeSpan or Enum => new ScalarValue(value),
            _ => new ScalarValue(UnsupportedScalar),
        };
    }

    private static bool IsRestrictedDetailKey(string key) =>
        IsRestrictedDetailKey(key.AsSpan());

    private static bool IsRestrictedDetailKey(ReadOnlySpan<char> key)
    {
        if (key.Length > 128)
        {
            return true;
        }

        Span<char> normalized = stackalloc char[128];
        var length = 0;
        foreach (var character in key)
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized[length++] = char.ToLowerInvariant(character);
            }
        }

        var name = normalized[..length];
        return name is "clientip" or "serverip" or "remoteip" or "localip"
            or "clientipaddress" or "serveripaddress" or "remoteipaddress"
            or "localipaddress" or "clientaddress" or "serveraddress"
            or "remoteaddress" or "localaddress" or "clientport" or "serverport"
            or "remoteport" or "localport" or "requestbody" or "responsebody"
            or "requestheaders" or "responseheaders" or "headers" or "querystring"
            or "rawquery" or "rawurl" or "requesturi" or "responsecontent"
            or "requestpayload" or "responsepayload";
    }

    private static bool IsApprovedPayload(
        string key,
        LogEventPropertyValue value,
        bool isHttpOperation)
    {
        if (!isHttpOperation
            || key is not ("RequestPayload" or "ResponsePayload")
            || value is not ScalarValue { Value: string json }
            || Encoding.UTF8.GetByteCount(json) > 2048)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 2,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var seen = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                int bit;
                switch (property.Name)
                {
                    case "page" when property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt32(out var page) && page >= 0:
                        bit = 1;
                        break;
                    case "pageSize" when property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt32(out var pageSize)
                        && pageSize is >= 1 and <= 1000:
                        bit = 2;
                        break;
                    case "totalCount" when key == "ResponsePayload"
                        && property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt64(out var totalCount) && totalCount >= 0:
                        bit = 4;
                        break;
                    case "itemCount" when key == "ResponsePayload"
                        && property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt32(out var itemCount) && itemCount >= 0:
                        bit = 8;
                        break;
                    default:
                        return false;
                }

                if ((seen & bit) != 0)
                {
                    return false;
                }

                seen |= bit;
            }

            if (seen != (key == "RequestPayload" ? 3 : 15))
            {
                return false;
            }

            if (key == "ResponsePayload"
                && document.RootElement.GetProperty("itemCount").GetInt32()
                    > document.RootElement.GetProperty("pageSize").GetInt32())
            {
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BoundString(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        var length = maxChars;
        if (char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }

    private static string SanitizeText(string value)
    {
        return ContainsSensitiveText(value)
            ? HttpOperationLogSanitizer.Redacted
            : value;
    }

    private static bool ContainsSensitiveText(string value)
    {
        if (value.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is not ('=' or ':'))
            {
                continue;
            }

            var end = index - 1;
            while (end >= 0 && (char.IsWhiteSpace(value[end])
                || value[end] is '\"' or '\'' or '\\'))
            {
                end--;
            }

            var start = end;
            while (start >= 0 && end - start < 128
                && (char.IsLetterOrDigit(value[start])
                    || value[start] is '_' or '-' or ' ' or '.' or '[' or ']'))
            {
                start--;
            }

            if (end >= start + 1
                && (HttpOperationLogSanitizer.IsSensitiveKey(
                        value.AsSpan(start + 1, end - start))
                    || IsRestrictedDetailKey(
                        value.AsSpan(start + 1, end - start))))
            {
                // 赋值形态可以出现在 Query、连接串或 JSON 文本里；不保留无法定位终点的尾部。
                return true;
            }
        }

        return false;
    }

    private static bool TryFormat(
        LogEvent safeEvent,
        int maxEventBytes,
        bool retainLegacyEvent,
        out LogEnvelope? envelope)
    {
        try
        {
            using var writer = new BoundedUtf8TextWriter(maxEventBytes);
            Formatter.Format(safeEvent, writer);
            var bytes = Encoding.UTF8.GetBytes(writer.GetText());
            if (bytes.Length > maxEventBytes)
            {
                envelope = null;
                return false;
            }

            var legacyCharge = retainLegacyEvent
                ? EstimateLegacyEventCharge(safeEvent)
                : 0;
            if (legacyCharge > LogEnvelope.MaxLegacyEventChargeBytes(maxEventBytes))
            {
                envelope = null;
                return false;
            }

            envelope = new LogEnvelope(
                bytes,
                retainLegacyEvent ? safeEvent : null,
                legacyCharge,
                safeEvent.Properties.TryGetValue("LogEventId", out var eventIdValue)
                && eventIdValue is ScalarValue { Value: string eventId }
                && eventId.Length == 36
                && Guid.TryParseExact(eventId, "D", out _)
                    ? eventId
                    : null);
            return true;
        }
        catch (LogEventTooLargeException)
        {
            envelope = null;
            return false;
        }
    }

    private static int EstimateLegacyEventCharge(LogEvent safeEvent)
    {
        var charge = 256 + safeEvent.MessageTemplate.Text.Length * 2;
        charge += safeEvent.MessageTemplate.Tokens.Count() * 96;
        if (safeEvent.Exception is not null)
        {
            charge += 256;
        }

        foreach (var property in safeEvent.Properties)
        {
            charge += 96 + property.Key.Length * 2;
            charge += EstimateValueCharge(property.Value);
        }

        return charge;
    }

    private static int EstimateValueCharge(LogEventPropertyValue value) => value switch
    {
        ScalarValue { Value: string text } => 64 + text.Length * 2,
        ScalarValue => 64,
        SequenceValue sequence => 96 + sequence.Elements.Sum(EstimateValueCharge),
        StructureValue structure => 96
            + (structure.TypeTag?.Length ?? 0) * 2
            + structure.Properties.Sum(property =>
                96 + property.Name.Length * 2 + EstimateValueCharge(property.Value)),
        DictionaryValue dictionary => 96 + dictionary.Elements.Sum(entry =>
            EstimateValueCharge(entry.Key) + EstimateValueCharge(entry.Value)),
        _ => 64,
    };

    private sealed class BoundedUtf8TextWriter(int maxBytes) : TextWriter
    {
        private readonly StringBuilder _text = new(Math.Min(256, maxBytes));
        private int _bytes;

        public override Encoding Encoding => Encoding.UTF8;

        public string GetText() => _text.ToString();

        public override void Write(char value)
        {
            Span<char> character = stackalloc char[1];
            character[0] = value;
            Append(character);
        }

        public override void Write(string? value)
        {
            if (value is not null)
            {
                Append(value.AsSpan());
            }
        }

        public override void Write(ReadOnlySpan<char> buffer) => Append(buffer);

        public override void Write(char[] buffer, int index, int count) =>
            Append(buffer.AsSpan(index, count));

        private void Append(ReadOnlySpan<char> value)
        {
            var bytes = Encoding.UTF8.GetByteCount(value);
            if (bytes > maxBytes - _bytes)
            {
                throw new LogEventTooLargeException();
            }

            _text.Append(value);
            _bytes += bytes;
        }
    }

    private sealed class LogEventTooLargeException : Exception;
}
