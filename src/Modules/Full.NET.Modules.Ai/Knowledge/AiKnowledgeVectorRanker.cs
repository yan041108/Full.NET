namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>在有界候选内计算余弦相似度；不同模型空间不能混排。</summary>
internal static class AiKnowledgeVectorRanker
{
    internal static IReadOnlyList<AiKnowledgeRankedChunk> Rank(ReadOnlyMemory<float> vector,
        IReadOnlyList<AiKnowledgeRetrievalChunk> chunks, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count > 256) throw Invalid();
        var norm = Norm(vector.Span, vector.Length, token);
        var seen = new HashSet<(Guid, int)>(); var ranked = new List<AiKnowledgeRankedChunk>(chunks.Count);
        foreach (var chunk in chunks)
        {
            token.ThrowIfCancellationRequested();
            if (chunk.Source.TaskId is not { } task || task == Guid.Empty || chunk.Source.DocumentId == Guid.Empty
                || chunk.Sequence < 0 || !seen.Add((task, chunk.Sequence))) throw Invalid();
            var other = Norm(chunk.Vector.Span, vector.Length, token); double dot = 0;
            for (var i = 0; i < vector.Length; i++) dot += (double)vector.Span[i] * chunk.Vector.Span[i];
            // 单精度乘积会溢出；双精度累加且分开开平方，避免中间范数乘积扩大。
            var score = dot / Math.Sqrt(norm) / Math.Sqrt(other);
            if (!double.IsFinite(score)) throw Invalid();
            ranked.Add(new(chunk, Math.Clamp(score, -1, 1)));
        }
        token.ThrowIfCancellationRequested();
        return Array.AsReadOnly(ranked.OrderByDescending(r => r.Score)
            .ThenBy(r => r.Chunk.Source.DocumentId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(r => r.Chunk.Sequence).ToArray());
    }
    private static double Norm(ReadOnlySpan<float> values, int dimensions, CancellationToken token)
    {
        if (dimensions is not (1536 or 3072) || values.Length != dimensions) throw Invalid();
        double sum = 0;
        foreach (var value in values)
        {
            token.ThrowIfCancellationRequested(); if (!float.IsFinite(value)) throw Invalid(); sum += (double)value * value;
        }
        if (sum <= 0 || !double.IsFinite(sum)) throw Invalid(); return sum;
    }
    private static InvalidDataException Invalid() => new("检索向量或候选集合无效。");
}
