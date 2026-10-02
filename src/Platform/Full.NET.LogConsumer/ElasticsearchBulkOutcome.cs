using System.Text.Json;

namespace Full.NET.LogConsumer;

/// <summary>单条 ES Bulk 写入的后续动作；Isolate 仍需可靠 DLQ ACK 才能完成 Kafka 记录。</summary>
public enum BulkItemOutcome
{
    /// <summary>目标文档已由 ES 确认。</summary>
    Succeeded,

    /// <summary>保持 Kafka 记录未完成并重试或停止。</summary>
    Retry,

    /// <summary>确定性文档错误，等待可靠隔离确认。</summary>
    Isolate,
}

/// <summary>Bulk 请求中一条 index 动作的固定索引与文档 ID。</summary>
public readonly record struct BulkItemTarget(string IndexName, string DocumentId);

/// <summary>把 ES Bulk 的逐项结果映射到原始批次位置，绝不把 HTTP 200 当作整批成功。</summary>
public static class ElasticsearchBulkOutcome
{
    /// <summary>解析一批 index 动作；协议响应不完整或矛盾时让整批保持未完成。</summary>
    /// <param name="httpStatus">HTTP 响应状态码。</param>
    /// <param name="body">有界读取的 ES Bulk 响应正文。</param>
    /// <param name="expectedItems">与请求顺序一致的 index 动作数量。</param>
    /// <param name="expectedTargets">需要确认的逐项索引与文档 ID；生产写入必须提供。</param>
    /// <returns>每条对应的后续动作；解析失败时全部为 Retry。</returns>
    public static BulkItemOutcome[] Parse(int httpStatus, string body, int expectedItems,
        IReadOnlyList<BulkItemTarget>? expectedTargets = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedItems);
        if (expectedTargets is not null && expectedTargets.Count != expectedItems)
        {
            throw new ArgumentException("确认目标数必须与请求动作数一致。", nameof(expectedTargets));
        }
        var retryAll = Enumerable.Repeat(BulkItemOutcome.Retry, expectedItems).ToArray();
        if (httpStatus is < 200 or > 299 || string.IsNullOrEmpty(body))
        {
            return retryAll;
        }

        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("errors", out var errors)
                || errors.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array
                || items.GetArrayLength() != expectedItems)
            {
                return retryAll;
            }

            var outcomes = new BulkItemOutcome[expectedItems];
            var hasFailure = false;
            for (var index = 0; index < expectedItems; index++)
            {
                var item = items[index];
                if (item.ValueKind != JsonValueKind.Object
                    || item.EnumerateObject().Count() != 1
                    || !item.TryGetProperty("index", out var operation)
                    || operation.ValueKind != JsonValueKind.Object
                    || !operation.TryGetProperty("status", out var statusElement)
                    || statusElement.ValueKind != JsonValueKind.Number
                    || !statusElement.TryGetInt32(out var status))
                {
                    return retryAll;
                }

                if (expectedTargets is not null
                    && (!operation.TryGetProperty("_index", out var responseIndex)
                        || responseIndex.ValueKind != JsonValueKind.String
                        || !string.Equals(responseIndex.GetString(), expectedTargets[index].IndexName,
                            StringComparison.Ordinal)
                        || !operation.TryGetProperty("_id", out var responseId)
                        || responseId.ValueKind != JsonValueKind.String
                        || !string.Equals(responseId.GetString(), expectedTargets[index].DocumentId,
                            StringComparison.Ordinal)))
                {
                    // 回执不能证明本次目标已处理时，保留原 Offset 等待重放。
                    return retryAll;
                }

                outcomes[index] = status switch
                {
                    >= 200 and <= 299 => BulkItemOutcome.Succeeded,
                    400 or 409 or 413 or 422 => BulkItemOutcome.Isolate,
                    _ => BulkItemOutcome.Retry,
                };
                if (outcomes[index] == BulkItemOutcome.Succeeded
                    && operation.TryGetProperty("error", out _))
                {
                    return retryAll;
                }

                hasFailure |= outcomes[index] != BulkItemOutcome.Succeeded;
            }

            // 顶层 errors 与逐项状态矛盾时，不能猜测哪一侧可信。
            return (errors.GetBoolean() == hasFailure) ? outcomes : retryAll;
        }
        catch (JsonException)
        {
            return retryAll;
        }
    }
}
