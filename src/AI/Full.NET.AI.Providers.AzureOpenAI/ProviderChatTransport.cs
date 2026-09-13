using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.AI.Abstractions.Models;
namespace Full.NET.AI.Providers.Internal;

/// <summary>Azure OpenAI 聊天传输；deployment 名称来自绑定 ModelId。</summary>
internal sealed class ProviderChatTransport(HttpClient client)
{
    internal Task<AiChatCompletionResult> StreamAsync(
        ModelBinding modelConfig,
        string? apiKey,
        IReadOnlyList<(string RoleKey, string Content)> messages,
        Func<string, Task> onDelta,
        StringBuilder buffer,
        CancellationToken cancellationToken,
        bool requestStreamingUsage = false)
    {
        if (requestStreamingUsage)
            throw new NotSupportedException("AI provider capability is not enabled.");
        return StreamAzureAsync(modelConfig, apiKey, messages, onDelta, buffer, cancellationToken);
    }

    private async Task<AiChatCompletionResult> StreamAzureAsync(
        ModelBinding modelConfig,
        string? apiKey,
        IReadOnlyList<(string RoleKey, string Content)> messages,
        Func<string, Task> onDelta,
        StringBuilder builder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API key is required for azure_openai provider.");

        var apiVersion = AzureOpenAI.AzureConnectivityProbe.ResolveApiVersion(modelConfig);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{modelConfig.Endpoint.AbsoluteUri.TrimEnd('/')}/openai/deployments/{modelConfig.ModelId}/chat/completions?api-version={apiVersion}");
        request.Headers.TryAddWithoutValidation("api-key", apiKey);
        var payload = new JsonObject
        {
            ["stream"] = true,
            ["max_tokens"] = 4096,
            ["messages"] = new JsonArray(messages.Select(item => (JsonNode)new JsonObject
            {
                ["role"] = item.RoleKey,
                ["content"] = item.Content,
            }).ToArray()),
        };
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("AI provider rejected the request.", null, response.StatusCode);

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
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                completed = true;
                break;
            }

            using var document = JsonDocument.Parse(data);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw new InvalidDataException("AI provider returned an error frame.");
            if (document.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var prompt)) promptTokens = prompt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var completion)) completionTokens = completion.GetInt32();
            }

            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;
            var delta = choices[0];
            if (delta.TryGetProperty("delta", out var deltaObject)
                && deltaObject.TryGetProperty("content", out var contentNode))
            {
                var text = contentNode.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    if (builder.Length + text.Length > 1024 * 1024)
                        throw new InvalidDataException("AI generated content limit exceeded.");
                    builder.Append(text);
                    await onDelta(text).ConfigureAwait(false);
                }
            }
        }

        if (!completed) throw new InvalidDataException("AI response ended before completion.");
        return new(builder.ToString(), promptTokens, completionTokens, cancellationToken.IsCancellationRequested);
    }
}
