using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.AI.Abstractions.Models;

namespace Full.NET.AI.Providers.Internal;

/// <summary>Ollama Embedding 传输。</summary>
internal sealed class ProviderEmbeddingTransport(HttpClient client) : IEmbeddingTransport
{
    /// <inheritdoc/>
    public void Dispose() => client.Dispose();

    /// <inheritdoc/>
    public async Task<EmbeddingBatchResult> GenerateAsync(
        ModelBinding binding,
        string? apiKey,
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        var vectors = new List<ReadOnlyMemory<float>>(inputs.Count);
        int? totalPromptTokens = null;
        foreach (var input in inputs)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{binding.Endpoint.AbsoluteUri.TrimEnd('/')}/api/embeddings");
            var payload = new JsonObject
            {
                ["model"] = binding.ModelId,
                ["input"] = input,
            };
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw MapFailure(response.StatusCode);

            var bytes = await ReadBoundedBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw new NotSupportedException("AI provider capability is not enabled.");

            if (!document.RootElement.TryGetProperty("embedding", out var embeddingNode)
                || embeddingNode.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("AI provider returned an invalid embedding response.");

            var values = new float[embeddingNode.GetArrayLength()];
            var index = 0;
            foreach (var value in embeddingNode.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number)
                    throw new InvalidDataException("AI provider returned an invalid embedding response.");
                values[index++] = value.GetSingle();
            }
            if (values.Length == 0 || values.Length > 8192)
                throw new InvalidDataException("AI provider returned an invalid embedding response.");
            vectors.Add(values);

            if (document.RootElement.TryGetProperty("prompt_eval_count", out var prompt)
                && prompt.ValueKind == JsonValueKind.Number)
                totalPromptTokens = (totalPromptTokens ?? 0) + prompt.GetInt32();
        }

        return new(vectors, totalPromptTokens);
    }

    private static Exception MapFailure(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound
            ? new NotSupportedException("AI provider capability is not enabled.")
            : new HttpRequestException("AI provider rejected the request.", null, statusCode);

    private static async Task<ReadOnlyMemory<byte>> ReadBoundedBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var bytes = new byte[8_388_609];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return bytes.AsMemory(0, length);
            length += read;
        }
        throw new InvalidDataException("AI embedding response exceeds the response limit.");
    }
}
