using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.AI.Abstractions.Models;
namespace Full.NET.AI.Providers.Internal;
/// <summary>供应商私有传输；业务模块仅消费中立客户端。</summary>
internal sealed class ProviderChatTransport(HttpClient client)
{
    internal Task<AiChatCompletionResult> StreamAsync(ModelBinding modelConfig, string? apiKey,
        IReadOnlyList<(string RoleKey, string Content)> messages, Func<string, Task> onDelta,
        StringBuilder buffer, CancellationToken cancellationToken, bool requestStreamingUsage = false) =>
        StreamOllamaAsync(modelConfig, messages, onDelta, buffer, cancellationToken);
    private async Task<AiChatCompletionResult> StreamOllamaAsync(
        ModelBinding modelConfig,
        IReadOnlyList<(string RoleKey, string Content)> messages,
        Func<string, Task> onDelta,
        StringBuilder builder,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{modelConfig.Endpoint.AbsoluteUri.TrimEnd('/')}/api/chat");
        var payload = new JsonObject
        {
            ["model"] = modelConfig.ModelId,
            ["stream"] = true,
            ["options"] = new JsonObject { ["num_predict"] = 4096 },
            ["messages"] = new JsonArray(messages.Select(item => (JsonNode)new JsonObject
            {
                ["role"] = item.RoleKey,
                ["content"] = item.Content,
            }).ToArray()),
        };
        request.Content = new StringContent(
            payload.ToJsonString(),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // 错误正文由供应商控制，不能进入客户端或异常消息。
            throw new HttpRequestException("AI provider rejected the request.", null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var lines = new AiResponseLineReader(reader);
        int? promptTokens = null;
        int? completionTokens = null;
        var completed = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await lines.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            RejectErrorFrame(document.RootElement);
            if (document.RootElement.TryGetProperty("prompt_eval_count", out var prompt))
            {
                promptTokens = prompt.GetInt32();
            }

            if (document.RootElement.TryGetProperty("eval_count", out var completion))
            {
                completionTokens = completion.GetInt32();
            }

            if (document.RootElement.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var contentNode))
            {
                // Ollama 返回增量而非累计正文，重复片段也是有效内容。
                var delta = contentNode.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(delta))
                {
                    AppendWithinBudget(builder, delta);
                    await onDelta(delta).ConfigureAwait(false);
                }
            }

            if (document.RootElement.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True)
            {
                completed = true;
                break;
            }
        }

        // EOF 不是供应商确认的完成；保留已接收正文供调用者标记失败。
        if (!completed) throw new InvalidDataException("AI response ended before completion.");
        return new AiChatCompletionResult(
            builder.ToString(),
            promptTokens,
            completionTokens,
            cancellationToken.IsCancellationRequested);
    }
    private static void RejectErrorFrame(JsonElement frame)
    {
        if (frame.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("AI provider returned an error frame.");
    }

    /// <summary>在发送及保留片段之前检查正文上限，限制重复字符串和持久化负载。</summary>
    /// <param name="builder">请求级唯一正文缓冲。</param>
    /// <param name="delta">提供程序返回的文本片段。</param>
    private static void AppendWithinBudget(StringBuilder builder, string delta)
    {
        if (builder.Length + (long)delta.Length > 1024 * 1024)
            throw new InvalidDataException("AI generated content limit exceeded.");
        builder.Append(delta);
    }
}
