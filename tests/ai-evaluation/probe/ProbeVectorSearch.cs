using System.Text;

namespace Full.NET.AiRetrieval.Probe;

/// <summary>仅验证选型算法约束；合成向量不代表真实 Embedding 或语义检索质量。</summary>
internal static class ProbeVectorSearch
{
    internal static float[] Embed(string text, int dimension = 128)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 2);
        var vector = new float[dimension];
        var normalized = text.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        // 固定 FNV-1a 二元字符特征，仅用于跨实现的一致性实验，不称为语义模型。
        for (var i = 0; i < normalized.Length - 1; i++)
        {
            var hash = unchecked((2166136261u ^ normalized[i]) * 16777619u);
            hash = unchecked((hash ^ normalized[i + 1]) * 16777619u);
            vector[hash % (uint)dimension]++;
        }
        var length = Math.Sqrt(vector.Sum(x => (double)x * x));
        if (length == 0) throw new InvalidDataException("合成向量不能为空。");
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / length);
        return vector;
    }

    internal static double Cosine(float[] query, float[] vector)
    {
        if (query.Length == 0 || query.Length != vector.Length) throw new InvalidDataException("向量维度不一致。");
        double dot = 0, left = 0, right = 0;
        for (var i = 0; i < query.Length; i++)
        {
            if (!float.IsFinite(query[i]) || !float.IsFinite(vector[i])) throw new InvalidDataException("向量存在非有限值。");
            dot += (double)query[i] * vector[i];
            left += (double)query[i] * query[i];
            right += (double)vector[i] * vector[i];
        }
        if (left == 0 || right == 0) throw new InvalidDataException("零向量不能进行余弦检索。");
        return dot / Math.Sqrt(left * right);
    }

    internal static string[] Rank(float[] query, IReadOnlyList<ProbeVector> candidates, string model,
        int generation, int maxCandidates, int k, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCandidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        cancellationToken.ThrowIfCancellationRequested();
        if (candidates.Count > maxCandidates) throw new InvalidDataException("候选超限，必须缩小权威来源范围。");
        var scored = new List<(string Id, double Score)>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Model != model || candidate.Generation != generation)
                throw new InvalidDataException("禁止混用模型或索引代次。");
            scored.Add((candidate.ChunkId, Cosine(query, candidate.Values)));
        }
        return scored.OrderByDescending(x => x.Score).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Take(k).Select(x => x.Id).ToArray();
    }
}

internal sealed record ProbeVector(string ChunkId, string Model, int Generation, float[] Values);
