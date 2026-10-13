namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>在有界候选内计算余弦相似度；不同模型空间不能混排。</summary>
internal static class AiKnowledgeVectorRanker
{
    internal static IReadOnlyList<AiKnowledgeRankedChunk> Rank(ReadOnlyMemory<float> vector,
        IReadOnlyList<AiKnowledgeRetrievalChunk> chunks, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(chunks);
        var count = chunks.Count; if (count is < 0 or > 256) throw Invalid();
        // 固化首次有界索引，外部集合计数或元素替换不能改变本次分类与排名候选。
        var frozen = new AiKnowledgeRetrievalChunk[count];
        for (var i = 0; i < count; i++) { token.ThrowIfCancellationRequested(); frozen[i] = chunks[i]; }
        // 空候选只是原查询向量校验，不虚构一次重排阶段。
        return count == 0 ? RankCore(vector, frozen, token)
            : AiKnowledgeTelemetry.Run("knowledge_rerank", () => RankCore(vector, frozen, token), token);
    }
    private static IReadOnlyList<AiKnowledgeRankedChunk> RankCore(ReadOnlyMemory<float> vector,
        IReadOnlyList<AiKnowledgeRetrievalChunk> chunks, CancellationToken token)
    {
        foreach (var chunk in chunks)
        {
            token.ThrowIfCancellationRequested();
            if (chunk is null || chunk.Source is null || !Domain.AiKnowledgePolicy.IsKnownClassification(chunk.Source.DataClassification)) throw Invalid();
        }
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
