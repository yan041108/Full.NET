using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Modules.Ai.Connectivity;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Streaming;

/// <summary>聊天完成结果。</summary>
/// <param name="Content">有界完整回复正文。</param>
/// <param name="PromptTokens">提供程序确认的提示用量。</param>
/// <param name="CompletionTokens">提供程序确认的生成用量。</param>
/// <param name="Cancelled">提供程序读取结束时是否已取消。</param>
internal sealed record AiChatCompletionResult(
    string Content,
    int? PromptTokens,
    int? CompletionTokens,
    bool Cancelled);

/// <summary>调用白名单提供程序执行流式聊天补全。</summary>
/// <param name="httpClientFactory">创建受配置约束的模型 HTTP 客户端。</param>
internal sealed class AiChatCompletionStreamer(IHttpClientFactory httpClientFactory)
{
    /// <summary>受配置约束的模型 HTTP 客户端名称。</summary>
    public const string HttpClientName = "Full.NET.Ai.ChatCompletion";

    /// <summary>执行流式补全并在每个增量片段时回调。</summary>
    /// <param name="modelConfig">模型配置。</param>
    /// <param name="apiKey">解保护后的 API 密钥。</param>
    /// <param name="messages">按时间排序的上下文消息。</param>
    /// <param name="onDelta">增量回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="contentBuffer">可选的请求级正文缓冲，失败时调用者也能保存已收到的片段。</param>
    /// <returns>完成结果。</returns>
    public async Task<AiChatCompletionResult> StreamAsync(
        AiModelConfigRecord modelConfig,
        string? apiKey,
        IReadOnlyList<(string RoleKey, string Content)> messages,
        Func<string, Task> onDelta,
        CancellationToken cancellationToken = default,
        StringBuilder? contentBuffer = null)
    {
        contentBuffer ??= new StringBuilder();
        if (string.Equals(modelConfig.ProviderKey, AiProviderKeys.OpenAiCompatible, StringComparison.Ordinal))
        {
            return await StreamOpenAiCompatibleAsync(modelConfig, apiKey, messages, onDelta, contentBuffer, cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.Equals(modelConfig.ProviderKey, AiProviderKeys.Ollama, StringComparison.Ordinal))
        {
            return await StreamOllamaAsync(modelConfig, messages, onDelta, contentBuffer, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException("Unsupported provider key.");
    }

    /// <summary>按 OpenAI 兼容协议发送闭合 JSON 请求并逐段转发响应。</summary>
    /// <param name="modelConfig">已验证的模型配置。</param>
    /// <param name="apiKey">仅用于当前请求认证的模型密钥。</param>
    /// <param name="messages">按对话顺序排列的角色与内容。</param>
    /// <param name="onDelta">消费增量文本的异步回调。</param>
    /// <param name="builder">唯一的请求正文缓冲，追加前检查上限。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    private async Task<AiChatCompletionResult> StreamOpenAiCompatibleAsync(
        AiModelConfigRecord modelConfig,
        string? apiKey,
        IReadOnlyList<(string RoleKey, string Content)> messages,
        Func<string, Task> onDelta,
        StringBuilder builder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("API key is required for openai_compatible provider.");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{AiModelConnectivityTester.NormalizeBaseUrl(modelConfig.EndpointBaseUrl)}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (!string.IsNullOrWhiteSpace(modelConfig.OrganizationId))
        {
            request.Headers.TryAddWithoutValidation("OpenAI-Organization", modelConfig.OrganizationId.Trim());
        }

        var payload = new JsonObject
        {
            ["model"] = modelConfig.ModelId,
            ["stream"] = true,
            ["max_tokens"] = AiChatContentPolicy.MaxCompletionTokens,
            ["stream_options"] = new JsonObject { ["include_usage"] = true },
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
            // 错误响应同样由外部服务控制，只读取有限前缀。
            await using var errorStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var errorReader = new StreamReader(errorStream);
            var errorPrefix = new char[512];
            var read = await errorReader.ReadBlockAsync(errorPrefix.AsMemory(), cancellationToken).ConfigureAwait(false);
            var body = new string(errorPrefix, 0, read);
            throw new HttpRequestException(
                AiChatContentPolicy.SanitizeExternalError(
                    $"HTTP {(int)response.StatusCode}: {body}"));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var lines = new AiResponseLineReader(reader);
        int? promptTokens = null;
        int? completionTokens = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await lines.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                break;
            }

            using var document = JsonDocument.Parse(data);
            if (document.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var prompt))
                {
                    promptTokens = prompt.GetInt32();
                }

                if (usage.TryGetProperty("completion_tokens", out var completion))
                {
                    completionTokens = completion.GetInt32();
                }
            }

            if (!document.RootElement.TryGetProperty("choices", out var choices)
                || choices.GetArrayLength() == 0)
            {
                continue;
            }

            var delta = choices[0];
            if (delta.TryGetProperty("delta", out var deltaObject)
                && deltaObject.TryGetProperty("content", out var contentNode))
            {
                var text = contentNode.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    AppendWithinBudget(builder, text);
                    await onDelta(text).ConfigureAwait(false);
                }
            }
        }

        return new AiChatCompletionResult(
            builder.ToString(),
            promptTokens,
            completionTokens,
            cancellationToken.IsCancellationRequested);
    }

    /// <summary>按 Ollama 协议发送消息并逐段读取生成结果。</summary>
    /// <param name="modelConfig">已验证的模型配置。</param>
    /// <param name="messages">按对话顺序排列的角色与内容。</param>
    /// <param name="onDelta">消费增量文本的异步回调。</param>
    /// <param name="builder">唯一的请求正文缓冲，追加前检查上限。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    private async Task<AiChatCompletionResult> StreamOllamaAsync(
        AiModelConfigRecord modelConfig,
        IReadOnlyList<(string RoleKey, string Content)> messages,
        Func<string, Task> onDelta,
        StringBuilder builder,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{AiModelConnectivityTester.NormalizeBaseUrl(modelConfig.EndpointBaseUrl)}/api/chat");
        var payload = new JsonObject
        {
            ["model"] = modelConfig.ModelId,
            ["stream"] = true,
            ["options"] = new JsonObject { ["num_predict"] = AiChatContentPolicy.MaxCompletionTokens },
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
            // 错误响应同样由外部服务控制，只读取有限前缀。
            await using var errorStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var errorReader = new StreamReader(errorStream);
            var errorPrefix = new char[512];
            var read = await errorReader.ReadBlockAsync(errorPrefix.AsMemory(), cancellationToken).ConfigureAwait(false);
            var body = new string(errorPrefix, 0, read);
            throw new HttpRequestException(
                AiChatContentPolicy.SanitizeExternalError(
                    $"HTTP {(int)response.StatusCode}: {body}"));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var lines = new AiResponseLineReader(reader);
        int? promptTokens = null;
        int? completionTokens = null;
        string? previous = string.Empty;
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
                var text = contentNode.GetString() ?? string.Empty;
                var delta = text.StartsWith(previous ?? string.Empty, StringComparison.Ordinal)
                    ? text[previous!.Length..]
                    : text;
                previous = text;
                if (!string.IsNullOrEmpty(delta))
                {
                    AppendWithinBudget(builder, delta);
                    await onDelta(delta).ConfigureAwait(false);
                }
            }
        }

        return new AiChatCompletionResult(
            builder.ToString(),
            promptTokens,
            completionTokens,
            cancellationToken.IsCancellationRequested);
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
