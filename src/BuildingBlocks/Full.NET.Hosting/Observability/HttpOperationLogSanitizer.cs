using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 普通 HTTP Operation Log 脱敏与截断；防止密码、Token、Cookie、连接串和日志注入进入 B2 流。
/// </summary>
public static partial class HttpOperationLogSanitizer
{
    // 旧字符串捕获入口的过渡上限；LG03 两阶段投影接入前不得解析或留存无界原文。
    internal const int MaxLegacyRawJsonBytes = 16_384;

    private static readonly string[] SensitiveKeyMarkers =
    [
        "password", "passwd", "pwd", "secret", "token", "authorization",
        "cookie", "connectionstring", "apikey", "signature", "privatekey",
        "nonce", "sessionid",
    ];

    public const string Redacted = "[REDACTED]";

    /// <summary>移除敏感 Query、截断长度，并剥离 CR/LF。</summary>
    public static string SanitizeUrl(string? url, int maxLength = 512)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "/";
        }

        var cleaned = StripControlChars(url);
        var hashIndex = cleaned.IndexOf('#');
        if (hashIndex >= 0)
        {
            cleaned = cleaned[..hashIndex];
        }

        var queryIndex = cleaned.IndexOf('?');
        if (queryIndex < 0)
        {
            return Truncate(cleaned, maxLength);
        }

        var path = cleaned[..queryIndex];
        var query = cleaned[(queryIndex + 1)..];
        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var eq = part.IndexOf('=');
            var key = eq >= 0 ? part[..eq] : part;
            if (IsSensitiveKey(key))
            {
                kept.Add(key + "=" + Redacted);
                continue;
            }

            var value = eq >= 0 ? part[(eq + 1)..] : string.Empty;
            kept.Add(key + "=" + Truncate(StripControlChars(value), 64));
        }

        var rebuilt = kept.Count == 0 ? path : path + "?" + string.Join('&', kept);
        return Truncate(rebuilt, maxLength);
    }

    /// <summary>Referer/Origin 仅保留 HTTP(S) 来源站点，不输出凭据、路径、Query 或 Fragment。</summary>
    public static string? SanitizeSourceUrl(string? sourceUrl, int maxLength = 256)
    {
        const int MaxInputLength = 2048;
        if (string.IsNullOrEmpty(sourceUrl)
            || sourceUrl.Length > MaxInputLength
            || maxLength <= 0)
        {
            return null;
        }

        var cleaned = StripControlChars(sourceUrl);
        if (!Uri.TryCreate(cleaned, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.IdnHost))
        {
            return null;
        }

        var host = uri.HostNameType == UriHostNameType.IPv6
            ? $"[{uri.IdnHost}]"
            : uri.IdnHost;
        var origin = uri.IsDefaultPort
            ? $"{uri.Scheme}://{host}"
            : $"{uri.Scheme}://{host}:{uri.Port}";
        return origin.Length <= maxLength ? origin : null;
    }

    public static string FingerprintClientIp(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(address)));
    }

    internal static string? FingerprintUntrustedValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
        {
            return null;
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>
    /// 按字段白名单投影 JSON；敏感键替换为 REDACTED，超深/超长截断。
    /// </summary>
    public static string? ProjectJsonPayload(
        string? rawJson,
        IReadOnlyCollection<string> allowedFields,
        int maxBytes,
        int maxDepth = 4)
    {
        if (maxBytes <= 0
            || string.IsNullOrWhiteSpace(rawJson)
            || allowedFields.Count == 0
            || rawJson.Length > MaxLegacyRawJsonBytes
            || Encoding.UTF8.GetByteCount(rawJson) > MaxLegacyRawJsonBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var field in allowedFields)
                {
                    if (!document.RootElement.TryGetProperty(field, out var value))
                    {
                        continue;
                    }

                    writer.WritePropertyName(field);
                    WriteSanitized(writer, field, value, depth: 0, maxDepth);
                }

                writer.WriteEndObject();
            }

            var bytes = stream.ToArray();
            if (bytes.Length > maxBytes)
            {
                // 字节切片可能截断多字节字符或 JSON token，超限时整份投影拒绝。
                return null;
            }

            return Encoding.UTF8.GetString(bytes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsSensitiveKey(string? key)
    {
        return key is not null && IsSensitiveKey(key.AsSpan());
    }

    internal static bool IsSensitiveKey(ReadOnlySpan<char> key)
    {
        key = key.Trim();
        if (key.IsEmpty)
        {
            return false;
        }

        // 超长动态键不构成可接受的日志字段；避免为它分配无界规范化缓冲。
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
        if (HasSignSegment(key))
        {
            return true;
        }

        foreach (var marker in SensitiveKeyMarkers)
        {
            if (name.Contains(marker.AsSpan(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSignSegment(ReadOnlySpan<char> key)
    {
        for (var index = 0; index <= key.Length - 4; index++)
        {
            if (!key.Slice(index, 4).Equals("sign", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var leftBoundary = index == 0
                || !char.IsLetterOrDigit(key[index - 1])
                || char.IsLower(key[index - 1]) && char.IsUpper(key[index]);
            if (!leftBoundary)
            {
                continue;
            }

            var after = index + 4;
            var suffix = key[after..];
            if ((suffix.StartsWith("In", StringComparison.OrdinalIgnoreCase)
                    && (suffix.Length == 2 || !char.IsLower(suffix[2])))
                || (suffix.StartsWith("Out", StringComparison.OrdinalIgnoreCase)
                    && (suffix.Length == 3 || !char.IsLower(suffix[3]))))
            {
                continue;
            }

            if (after == key.Length
                || !char.IsLower(key[after]))
            {
                return true;
            }

            // SigningKey 是签名材料；DesignId/AssignmentId 中的字母片段不是字段标记。
            if (key[after..].StartsWith("ing", StringComparison.OrdinalIgnoreCase))
            {
                var afterSigning = after + 3;
                if (afterSigning == key.Length || !char.IsLower(key[afterSigning]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    public static string StripControlChars(string value) =>
        ControlCharRegex().Replace(value, string.Empty);

    private static void WriteSanitized(
        Utf8JsonWriter writer,
        string propertyName,
        JsonElement element,
        int depth,
        int maxDepth)
    {
        if (IsSensitiveKey(propertyName))
        {
            writer.WriteStringValue(Redacted);
            return;
        }

        if (depth >= maxDepth)
        {
            writer.WriteStringValue("[TRUNCATED]");
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteSanitized(writer, property.Name, property.Value, depth + 1, maxDepth);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                var count = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (count++ >= 16)
                    {
                        writer.WriteStringValue("[TRUNCATED]");
                        break;
                    }

                    WriteSanitized(writer, propertyName, item, depth + 1, maxDepth);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(Truncate(StripControlChars(element.GetString() ?? string.Empty), 128));
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var longValue))
                {
                    writer.WriteNumberValue(longValue);
                }
                else
                {
                    writer.WriteNumberValue(element.GetDouble());
                }

                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteStringValue(Redacted);
                break;
        }
    }

    [GeneratedRegex(@"[\r\n\u0000-\u001F\u007F]", RegexOptions.CultureInvariant)]
    private static partial Regex ControlCharRegex();
}
