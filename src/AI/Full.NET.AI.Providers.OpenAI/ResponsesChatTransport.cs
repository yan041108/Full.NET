using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;

namespace Full.NET.AI.Providers.Internal;

/// <summary>显式批准的 Responses 文本协议；同一冻结输入先计数、再复核资格，绝不跨协议套用计量。</summary>
internal sealed class ResponsesChatTransport(HttpClient client) : IAiTextChatTransport
{
    public async Task<AiChatCompletionResult> StreamAsync(ModelBinding binding, string? apiKey,
        IReadOnlyList<(string RoleKey, string Content)> messages, Func<string, Task> onDelta,
        StringBuilder buffer, CancellationToken token, bool requestStreamingUsage, int maxOutputTokens)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("API key is required for the configured provider.");
        if (messages.Count is < 1 or > 256 || messages.Any(item => item.RoleKey is not ("system" or "developer" or "user" or "assistant"))
            || messages.Sum(item => (long)item.Content.Length) > 1_048_576) throw new AiBudgetException("ai.budget.invalid_request");
        var admission = AiChatInputBudgetScope.RequireCurrent();
        var input = new JsonArray(messages.Select(item => (JsonNode)new JsonObject { ["role"] = item.RoleKey, ["content"] = item.Content }).ToArray());
        var countPayload = new JsonObject { ["model"] = binding.ModelId, ["input"] = input };
        var generationPayload = new JsonObject { ["model"] = binding.ModelId, ["input"] = input.DeepClone(),
            ["store"] = false, ["stream"] = true, ["max_output_tokens"] = maxOutputTokens };
        // 两个请求的输入在第一次等待前复制完成；不引用可变历史或供应商选项。
        var countJson = countPayload.ToJsonString(); var generationJson = generationPayload.ToJsonString();
        var organization = binding.Options?.GetValueOrDefault("organization_id")?.Trim();
        await admission.RevalidateAsync(token).ConfigureAwait(false);
        using var countRequest = Request(binding, apiKey, organization, "responses/input_tokens", countJson);
        using var countResponse = await client.SendAsync(countRequest, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        RequireSuccess(countResponse);
        var counted = await ReadCountAsync(countResponse, token).ConfigureAwait(false);
        if (counted > admission.InputTokenLimit) throw new AiBudgetException("ai.budget.limit_exceeded");
        await admission.RevalidateAsync(token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
        using var generate = Request(binding, apiKey, organization, "responses", generationJson);
        using var response = await client.SendAsync(generate, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        RequireSuccess(response);
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream); var lines = new AiResponseLineReader(reader);
        while (await lines.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            token.ThrowIfCancellationRequested();
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            using var document = JsonDocument.Parse(line[5..].Trim()); var frame = document.RootElement;
            if (frame.ValueKind != JsonValueKind.Object || !frame.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) throw Invalid();
            switch (type.GetString())
            {
                case "response.output_text.delta":
                    if (!frame.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.String) throw Invalid();
                    var text = delta.GetString()!;
                    if (buffer.Length + (long)text.Length > 1_048_576) throw Invalid();
                    buffer.Append(text); await onDelta(text).ConfigureAwait(false); break;
                case "response.completed":
                    if (!frame.TryGetProperty("response", out var completed) || completed.ValueKind != JsonValueKind.Object
                        || !completed.TryGetProperty("status", out var status) || status.GetString() != "completed"
                        || !completed.TryGetProperty("model", out var model) || model.GetString() != binding.ModelId
                        || !completed.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) throw Invalid();
                    var prompt = Usage(usage, "input_tokens"); var output = Usage(usage, "output_tokens");
                    // 计数不是收费回执；缺少用量保持未知，矛盾回执不得用计数值覆盖。
                    if (prompt.HasValue && prompt != counted || output > maxOutputTokens) throw Invalid();
                    token.ThrowIfCancellationRequested(); return new(buffer.ToString(), prompt, output, false);
                case "response.failed": case "response.incomplete": case "error": throw Invalid();
            }
        }
        throw new InvalidDataException("AI response ended before completion.");
    }

    private static HttpRequestMessage Request(ModelBinding binding, string key, string? organization, string path, string payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, binding.Endpoint.AbsoluteUri.TrimEnd('/') + "/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (!string.IsNullOrEmpty(organization)) request.Headers.Add("OpenAI-Organization", organization);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json"); return request;
    }

    private static async Task<int> ReadCountAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength is > 8192) throw Invalid();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var bytes = new byte[8193]; var length = 0;
        while (length < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(length), token).ConfigureAwait(false);
            if (read == 0) break; length += read;
        }
        if (length == bytes.Length) throw Invalid();
        using var json = JsonDocument.Parse(bytes.AsMemory(0, length), new() { MaxDepth = 8 }); var value = json.RootElement;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 2
            || !value.TryGetProperty("object", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "response.input_tokens"
            || !value.TryGetProperty("input_tokens", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var result) || result < 0) throw Invalid();
        return result;
    }

    private static int? Usage(JsonElement usage, string name)
    {
        if (!usage.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (usage.EnumerateObject().Count(property => property.Name == name) != 1 || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var count) || count < 0) throw Invalid();
        return count;
    }
    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("AI provider rejected the request.", null, response.StatusCode);
    }
    private static InvalidDataException Invalid() => new("AI provider returned an invalid exact-input response.");
}
