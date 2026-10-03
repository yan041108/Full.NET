using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Full.NET.LogConsumer;

/// <summary>单条已解析日志及冻结目标索引；批量回执按输入顺序对应。</summary>
public readonly record struct LogDocumentWrite(ParsedLogRecord Record, string IndexName);

/// <summary>将已校验记录按固定索引与事件 ID 写入 ES，并逐项解释 Bulk 回执。</summary>
public sealed partial class ElasticsearchLogDocumentSink : ILogDocumentSink, ILogDocumentBatchSink
{
    private const int MaxResponseBytes = 64 * 1024;
    private const int MaxBatchItems = 64;
    private const int MaxRequestBytes = 1024 * 1024;
    private readonly HttpClient _http;
    private readonly Uri _bulkUri;
    private readonly TimeSpan _operationTimeout;

    /// <summary>创建 HTTPS Bulk 输出；认证由调用方配置，整次请求及响应读取使用有限超时。</summary>
    public ElasticsearchLogDocumentSink(
        HttpClient http, Uri baseUri, TimeSpan? operationTimeout = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        ArgumentNullException.ThrowIfNull(baseUri);
        _operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(30);
        if (_operationTimeout < TimeSpan.FromMilliseconds(1)
            || _operationTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        }
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps
            || baseUri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new ArgumentException("ES 地址必须为无查询参数的 HTTPS 绝对地址。", nameof(baseUri));
        }

        _bulkUri = new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/_bulk");
    }

    /// <inheritdoc />
    public async Task<BulkItemOutcome> WriteAsync(
        ParsedLogRecord record, string indexName, CancellationToken cancellationToken)
        => (await WriteBatchAsync([new LogDocumentWrite(record, indexName)], cancellationToken))[0];

    /// <summary>发送最多 64 条/1 MiB 的 index 批次，按请求顺序返回逐项结果；不确认 DLQ 或提交 Offset。</summary>
    public async Task<BulkItemOutcome[]> WriteBatchAsync(
        IReadOnlyList<LogDocumentWrite> writes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writes);
        var count = writes.Count;
        if (count is < 1 or > MaxBatchItems)
        {
            throw new ArgumentOutOfRangeException(nameof(writes), "Bulk 条数须为 1..64。");
        }
        cancellationToken.ThrowIfCancellationRequested();
        // 冻结调用方集合；预算通过前只保留已有载荷切片和有限动作头，不分配整批正文。
        var batch = new LogDocumentWrite[count];
        for (var index = 0; index < count; index++) batch[index] = writes[index];
        var prefixes = new byte[batch.Length][];
        var bodyLengths = new int[batch.Length];
        var targets = new BulkItemTarget[batch.Length];
        var length = 0;
        for (var index = 0; index < batch.Length; index++)
        {
            var write = batch[index];
            ArgumentNullException.ThrowIfNull(write.Record);
            if (string.IsNullOrEmpty(write.IndexName) || !IndexNameRegex().IsMatch(write.IndexName))
            {
                throw new ArgumentException("ES 索引名不符合日志固定路由。", nameof(writes));
            }
            var action = JsonSerializer.Serialize(new { index = new { _index = write.IndexName, _id = write.Record.LogEventId } });
            prefixes[index] = Encoding.UTF8.GetBytes(action + "\n");
            var json = write.Record.Utf8Json;
            while (!json.IsEmpty && json[^1] is (byte)'\r' or (byte)'\n') json = json[..^1];
            if (json.Length > MaxRequestBytes - length - prefixes[index].Length - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(writes), "Bulk 请求超过 1 MiB 字节预算。");
            }
            bodyLengths[index] = json.Length;
            length += prefixes[index].Length + json.Length + 1;
            targets[index] = new BulkItemTarget(write.IndexName, write.Record.LogEventId);
        }
        var payload = new byte[length];
        var position = 0;
        for (var index = 0; index < batch.Length; index++)
        {
            prefixes[index].CopyTo(payload, position);
            position += prefixes[index].Length;
            batch[index].Record.Utf8Json[..bodyLengths[index]].CopyTo(payload.AsSpan(position));
            position += bodyLengths[index];
            payload[position++] = (byte)'\n';
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_operationTimeout);
        var operationToken = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, _bulkUri)
        {
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
        using var response = await _http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, operationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(operationToken);
        var buffer = new byte[MaxResponseBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), operationToken);
            if (read == 0) break;
            total += read;
        }

        if (total > MaxResponseBytes)
        {
            return Enumerable.Repeat(BulkItemOutcome.Retry, batch.Length).ToArray();
        }

        return ElasticsearchBulkOutcome.Parse((int)response.StatusCode,
            Encoding.UTF8.GetString(buffer, 0, total), batch.Length, targets);
    }

    [GeneratedRegex(@"^fn-logs-[1-9][0-9]{0,3}-(diagnostic|http\.operation|security)-[0-9]{4}\.[0-9]{2}\.[0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex IndexNameRegex();
}
