using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.Abstractions.Observability;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ObservabilityAdmin.Features.QueryLogTimeline;

/// <summary>只读取受控索引的关联摘要；禁止任意 DSL、原文返回、重定向和无界响应。</summary>
internal sealed class ElasticsearchTimelineReader(IHttpClientFactory clients,
    IOptions<RuntimeLogTimelineOptions> options) : ILogTimelineReader
{
    public string Source => "runtime.elasticsearch";
    public string RequiredPermission => "observability.log_search.read";

    public async Task<LogTimelineSourceResult> ReadAsync(LogTimelineQuery query, CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (!config.ElasticsearchEnabled) return new([], "not_configured");
        if (!ValidConfiguration(config)) return new([], "unavailable");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(config.ElasticsearchEndpoint), config.ElasticsearchIndexPattern + "/_search?allow_partial_search_results=false"));
        request.Content = new ByteArrayContent(BuildQuery(query));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (!string.IsNullOrEmpty(config.ElasticsearchApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", config.ElasticsearchApiKey);
        using var client = clients.CreateClient(nameof(ElasticsearchTimelineReader));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return new([], "unavailable");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[1024 * 1024 + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        if (count == buffer.Length) return new([], "truncated");
        using var document = JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (!root.TryGetProperty("hits", out var hitsObject) || !hitsObject.TryGetProperty("hits", out var hits)
            || hits.ValueKind != JsonValueKind.Array) return new([], "unavailable");
        var entries = new List<LogTimelineEntry>();
        var malformed = false;
        foreach (var hit in hits.EnumerateArray().Take(query.Limit + 1))
        {
            if (hit.TryGetProperty("_source", out var source)
                && RuntimeLogTimelineParser.Parse(source, Source, query) is { } entry) entries.Add(entry);
            else malformed = true;
        }
        var partial = malformed || hits.GetArrayLength() > query.Limit
            || root.TryGetProperty("timed_out", out var timedOut) && timedOut.ValueKind == JsonValueKind.True
            || root.TryGetProperty("_shards", out var shards) && shards.TryGetProperty("failed", out var failed) && failed.GetInt32() > 0;
        return new(entries.Take(query.Limit).ToArray(), partial ? "truncated" : "available");
    }

    internal static bool ValidConfiguration(RuntimeLogTimelineOptions config) =>
        Uri.TryCreate(config.ElasticsearchEndpoint, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.AbsolutePath == "/" && uri.UserInfo.Length == 0
        && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && config.ElasticsearchIndexPattern.StartsWith("fn-logs-", StringComparison.Ordinal)
        && config.ElasticsearchIndexPattern.Length <= 128
        && config.ElasticsearchIndexPattern.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '*');

    internal static byte[] BuildQuery(LogTimelineQuery query)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("size", query.Limit + 1); writer.WriteBoolean("track_total_hits", false);
            writer.WriteString("timeout", "1500ms");
            writer.WriteStartArray("_source");
            foreach (var field in new[] { "@t", "@tr", "@sp", "LogEventId", "TraceId", "SpanId", "ParentSpanId",
                "RequestId", "UserId", "TenantId", "SessionId", "CenterSessionId", "ApplicationSessionId", "ClientIpFingerprint", "log.class" })
                writer.WriteStringValue(field);
            writer.WriteEndArray();
            writer.WriteStartArray("sort"); writer.WriteStringValue("@t");
            writer.WriteStartObject(); writer.WriteStartObject("LogEventId.keyword");
            writer.WriteString("order", "asc"); writer.WriteString("unmapped_type", "keyword");
            writer.WriteEndObject(); writer.WriteEndObject(); writer.WriteEndArray();
            writer.WriteStartObject("query"); writer.WriteStartObject("bool"); writer.WriteStartArray("filter");
            writer.WriteStartObject(); writer.WriteStartObject("range"); writer.WriteStartObject("@t");
            writer.WriteString("gte", query.FromUtc); writer.WriteString("lt", query.ToUtc);
            writer.WriteEndObject(); writer.WriteEndObject(); writer.WriteEndObject();
            Terms(["TraceId", "@tr"], query.TraceId);
            Terms(["UserId"], query.UserId?.ToString("D"));
            Terms(["SessionId", "CenterSessionId", "ApplicationSessionId"], query.SessionId?.ToString("D"));
            Terms(["ClientIpFingerprint"], query.ClientIpFingerprint);
            writer.WriteEndArray(); writer.WriteEndObject(); writer.WriteEndObject(); writer.WriteEndObject();

            void Terms(string[] names, string? value)
            {
                if (value is null) return;
                writer.WriteStartObject(); writer.WriteStartObject("bool"); writer.WriteNumber("minimum_should_match", 1);
                writer.WriteStartArray("should");
                foreach (var name in names)
                    foreach (var suffix in new[] { "", ".keyword" })
                    {
                        writer.WriteStartObject(); writer.WriteStartObject("term");
                        writer.WriteString(name + suffix, value); writer.WriteEndObject(); writer.WriteEndObject();
                    }
                writer.WriteEndArray(); writer.WriteEndObject(); writer.WriteEndObject();
            }
        }
        return stream.ToArray();
    }
}
