using Full.NET.AI.Abstractions.Models;
using Microsoft.Extensions.AI;

namespace Full.NET.AI.Providers.Internal;

/// <summary>将供应商 Embedding 响应映射为中立 <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>。</summary>
internal sealed class HttpEmbeddingClient(ModelBinding binding, string? apiKey, IEmbeddingTransport transport)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    /// <inheritdoc/>
    public EmbeddingGeneratorMetadata Metadata => new(binding.ModelId);

    /// <inheritdoc/>
    public void Dispose() => transport.Dispose();

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(IEmbeddingGenerator<string, Embedding<float>>) ? this : null;

    /// <inheritdoc/>
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (options is not null)
            throw new NotSupportedException("AI provider capability is not enabled.");

        var inputs = values.ToArray();
        if (inputs.Length == 0)
            throw new ArgumentException("At least one embedding input is required.", nameof(values));

        ValidateInputs(inputs);
        var batch = await transport.GenerateAsync(binding, apiKey, inputs, cancellationToken).ConfigureAwait(false);
        if (batch.Vectors.Count != inputs.Length)
            throw new InvalidDataException("AI provider returned an unexpected embedding count.");

        var embeddings = batch.Vectors.Select(vector => new Embedding<float>(vector)).ToArray();
        UsageDetails? usage = batch.InputTokens is int tokens
            ? new UsageDetails { InputTokenCount = tokens, OutputTokenCount = 0 }
            : null;
        return new GeneratedEmbeddings<Embedding<float>>(embeddings) { Usage = usage };
    }

    /// <summary>在派发前限制输入规模，避免超大正文进入外发请求。</summary>
    internal static void ValidateInputs(IReadOnlyList<string> inputs)
    {
        if (inputs.Count > 16)
            throw new NotSupportedException("AI provider capability is not enabled.");

        long total = 0;
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Embedding input cannot be empty.");
            if (input.Length > 8000)
                throw new NotSupportedException("AI provider capability is not enabled.");
            total = checked(total + input.Length);
            if (total > 32000)
                throw new NotSupportedException("AI provider capability is not enabled.");
        }
    }
}
