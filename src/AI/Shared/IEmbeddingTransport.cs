using Full.NET.AI.Abstractions.Models;

namespace Full.NET.AI.Providers.Internal;

/// <summary>供应商私有 Embedding 传输；业务模块仅消费中立生成器。</summary>
internal interface IEmbeddingTransport : IDisposable
{
    /// <summary>对一批输入生成向量；不支持的能力在派发前或供应商错误时显式失败。</summary>
    Task<EmbeddingBatchResult> GenerateAsync(
        ModelBinding binding,
        string? apiKey,
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken);
}
