using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Ai.Connectivity;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Streaming;

/// <summary>聊天完成结果。</summary>
internal sealed record AiChatCompletionResult(
    string Content,
    int? PromptTokens,
    int? CompletionTokens,
    bool Cancelled);

/// <summary>调用白名单提供程序执行流式聊天补全。</summary>
internal sealed class AiChatCompletionStreamer(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "Full.NET.Ai.ChatCompletion";

    /// <summary>执行流式补全并在每个增量片段时回调。</summary>
    /// <param name="modelConfig">模型配置。</param>
    /// <param name="apiKey">解保护后的 API 密钥。</param>
    /// <param name="messages">按时间排序的上下文消息。</param>
    /// <param name="onDelta">增量回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成结果。</returns>
    public async Task<AiChatCompletionResult> StreamAsync(
        AiModelConfigRecord modelConfig,
        string? apiKey,
        IReadOnlyList<(string RoleKey, string Content)> messages,
        Func<string, Task> onDelta,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(modelConfig.ProviderKey, AiProviderKeys.OpenAiCompatible, StringComparison.Ordinal))
        {
            return await StreamOpenAiCompatibleAsync(modelConfig, apiKey, messages, onDelta, cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.Equals(modelConfig.ProviderKey, AiProviderKeys.Ollama, StringComparison.Ordinal))
        {
            return await StreamOllamaAsync(modelConfig, messages, onDelta, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException("Unsupported provider key.");
    }

    private async Task<AiChatCompletionResult> StreamOpenAiCompatibleAsync(
        AiModelConfigRecord modelConfig,
        string? apiKey,
        IReadOnlyList<(string RoleKey, string Content)> messages,
        Func<string, Task> onDelta,
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

        var payload = new
        {
            model = modelConfig.ModelId,
            stream = true,
            messages = messages.Select(item => new { role = item.RoleKey, content = item.Content }).ToArray(),
        };
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                AiChatContentPolicy.SanitizeExternalError(
                    $"HTTP {(int)response.StatusCode}: {body}"));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var builder = new StringBuilder();
        int? promptTokens = null;
        int? completionTokens = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
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
            if (document.RootElement.TryGetProperty("usage", out var usage))
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
                    builder.Append(text);
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

    private async Task<AiChatCompletionResult> StreamOllamaAsync(
        AiModelConfigRecord modelConfig,
        IReadOnlyList<(string RoleKey, string Content)> messages,
        Func<string, Task> onDelta,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{AiModelConnectivityTester.NormalizeBaseUrl(modelConfig.EndpointBaseUrl)}/api/chat");
        var payload = new
        {
            model = modelConfig.ModelId,
            stream = true,
            messages = messages.Select(item => new { role = item.RoleKey, content = item.Content }).ToArray(),
        };
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                AiChatContentPolicy.SanitizeExternalError(
                    $"HTTP {(int)response.StatusCode}: {body}"));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var builder = new StringBuilder();
        int? promptTokens = null;
        int? completionTokens = null;
        string? previous = string.Empty;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
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
                    builder.Append(delta);
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
}
