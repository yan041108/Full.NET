using System.Security.Cryptography;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>仅供单次操作共享的完整清单验证结果；不携带授权或费用资格。</summary>
internal sealed class AiKnowledgeIndexDocumentVerifiedManifest
{
    internal AiKnowledgeDocumentPlan Plan { get; }
    internal string PayloadHash { get; }
    internal AiKnowledgeIndexDocumentVectorIdentity VectorIdentity { get; }
    private AiKnowledgeIndexDocumentVerifiedManifest(AiKnowledgeDocumentPlan plan, string hash, CancellationToken token)
    { Plan = plan; PayloadHash = hash; VectorIdentity = AiKnowledgeIndexDocumentVectorCodec.Identity(plan, token); }

    /// <summary>冻结实际字节后重建完整正文及坐标；调用方摘要不参与构造。</summary>
    internal static AiKnowledgeIndexDocumentVerifiedManifest Capture(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 8 or > AiKnowledgeIndexDocumentManifestCodec.MaxPayloadBytes) throw new InvalidDataException("Invalid 多格式文档manifest.");
        var owned = bytes.ToArray();
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(owned));
            var plan = AiKnowledgeIndexDocumentManifestCodec.Decode(owned, token);
            token.ThrowIfCancellationRequested(); return new(plan, hash, token);
        }
        finally { CryptographicOperations.ZeroMemory(owned); }
    }

    /// <summary>深比较完整计划；批次中的只读集合不能用引用相等替代内容一致。</summary>
    internal static bool SamePlan(AiKnowledgeDocumentPlan left, AiKnowledgeDocumentPlan right)
    {
        var a = left.Source; var b = right.Source;
        return left.Summary == right.Summary && a.KnowledgeBaseId == b.KnowledgeBaseId && a.KnowledgeBaseVersion == b.KnowledgeBaseVersion
            && a.TenantId == b.TenantId && a.ModelConfigId == b.ModelConfigId && a.ModelConfigVersion == b.ModelConfigVersion
            && a.ProviderKey == b.ProviderKey && a.ModelId == b.ModelId && a.Options == b.Options && a.TokenizerVersion == b.TokenizerVersion
            && a.ParserRuntimeId == b.ParserRuntimeId && a.DataClassification == b.DataClassification && a.DestinationHash == b.DestinationHash
            && a.Document.ContentHash == b.Document.ContentHash && a.Document.SizeBytes == b.Document.SizeBytes
            && a.Document.Format == b.Document.Format && a.Document.ParserVersion == b.Document.ParserVersion && a.Document.Policy == b.Document.Policy
            && a.Document.Blocks.SequenceEqual(b.Document.Blocks) && a.Chunks.SequenceEqual(b.Chunks)
            && left.Batches.Count == right.Batches.Count && left.Batches.Zip(right.Batches).All(pair => pair.First.Ordinal == pair.Second.Ordinal
                && pair.First.SourceHash == pair.Second.SourceHash && pair.First.Chunks.SequenceEqual(pair.Second.Chunks));
    }
}
