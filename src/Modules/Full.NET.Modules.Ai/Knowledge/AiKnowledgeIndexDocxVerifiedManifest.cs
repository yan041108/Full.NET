using System.Security.Cryptography;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>仅供单次操作共享的完整清单验证结果；不携带授权或费用资格。</summary>
internal sealed class AiKnowledgeIndexDocxVerifiedManifest
{
    internal AiKnowledgeIndexDocxPlan Plan { get; }
    internal string PayloadHash { get; }
    internal AiKnowledgeIndexDocxVectorIdentity VectorIdentity { get; }
    private AiKnowledgeIndexDocxVerifiedManifest(AiKnowledgeIndexDocxPlan plan, string hash, CancellationToken token)
    { Plan = plan; PayloadHash = hash; VectorIdentity = AiKnowledgeIndexDocxVectorCodec.Identity(plan, token); }

    /// <summary>冻结实际字节后重建完整正文及坐标；调用方摘要不参与构造。</summary>
    internal static AiKnowledgeIndexDocxVerifiedManifest Capture(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 8 or > AiKnowledgeIndexDocxManifestCodec.MaxPayloadBytes) throw new InvalidDataException("Invalid DOCX manifest.");
        var owned = bytes.ToArray();
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(owned));
            var plan = AiKnowledgeIndexDocxManifestCodec.Decode(owned, token);
            token.ThrowIfCancellationRequested(); return new(plan, hash, token);
        }
        finally { CryptographicOperations.ZeroMemory(owned); }
    }

    /// <summary>深比较完整计划；批次中的只读集合不能用引用相等替代内容一致。</summary>
    internal static bool SamePlan(AiKnowledgeIndexDocxPlan left, AiKnowledgeIndexDocxPlan right)
    {
        var a = left.Source; var b = right.Source;
        return left.Summary == right.Summary && a.KnowledgeBaseId == b.KnowledgeBaseId && a.KnowledgeBaseVersion == b.KnowledgeBaseVersion
            && a.TenantId == b.TenantId && a.ModelConfigId == b.ModelConfigId && a.ModelConfigVersion == b.ModelConfigVersion
            && a.ProviderKey == b.ProviderKey && a.ModelId == b.ModelId && a.Options == b.Options && a.TokenizerVersion == b.TokenizerVersion
            && a.Document.ContentHash == b.Document.ContentHash && a.Document.SizeBytes == b.Document.SizeBytes
            && a.Document.Text == b.Document.Text && a.Document.Blocks.SequenceEqual(b.Document.Blocks) && a.Chunks.SequenceEqual(b.Chunks)
            && left.Batches.Count == right.Batches.Count && left.Batches.Zip(right.Batches).All(pair => pair.First.Ordinal == pair.Second.Ordinal
                && pair.First.SourceHash == pair.Second.SourceHash && pair.First.Chunks.SequenceEqual(pair.Second.Chunks));
    }
}
