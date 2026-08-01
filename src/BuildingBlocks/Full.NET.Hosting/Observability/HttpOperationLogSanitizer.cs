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
    private static readonly string[] SensitiveKeyMarkers =
    [
        "password",
        "passwd",
        "pwd",
        "secret",
        "token",
        "access_token",
        "refresh_token",
        "authorization",
        "cookie",
        "set-cookie",
        "connectionstring",
        "connection_string",
        "api_key",
        "apikey",
        "sign",
        "signature",
        "private_key",
        "client_secret",
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

    /// <summary>Referer/Origin 仅观测用途；默认去掉 Query 并截断。</summary>
    public static string? SanitizeSourceUrl(string? sourceUrl, int maxLength = 256)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return null;
        }

        var cleaned = StripControlChars(sourceUrl);
        var queryIndex = cleaned.IndexOf('?');
        if (queryIndex >= 0)
        {
            cleaned = cleaned[..queryIndex];
        }

        return Truncate(cleaned, maxLength);
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

    /// <summary>
    /// 按字段白名单投影 JSON；敏感键替换为 REDACTED，超深/超长截断。
    /// </summary>
    public static string? ProjectJsonPayload(
        string? rawJson,
        IReadOnlyCollection<string> allowedFields,
        int maxBytes,
        int maxDepth = 4)
    {
        if (maxBytes <= 0 || string.IsNullOrWhiteSpace(rawJson) || allowedFields.Count == 0)
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
                return Encoding.UTF8.GetString(bytes.AsSpan(0, maxBytes)) + "…";
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
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = key.Trim().ToLowerInvariant();
        return SensitiveKeyMarkers.Any(marker =>
            normalized.Equals(marker, StringComparison.Ordinal)
            || normalized.Contains(marker, StringComparison.Ordinal));
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
