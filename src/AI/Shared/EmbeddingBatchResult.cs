namespace Full.NET.AI.Providers.Internal;

/// <summary>供应商 Embedding 批次结果；向量只在 Provider 内解析，业务层按维度与计量消费。</summary>
/// <param name="Vectors">与输入顺序一致的向量列表。</param>
/// <param name="InputTokens">提供程序返回的输入 Token 计量；缺失时为 <see langword="null"/>。</param>
internal sealed record EmbeddingBatchResult(IReadOnlyList<ReadOnlyMemory<float>> Vectors, int? InputTokens);
