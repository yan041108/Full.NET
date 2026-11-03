using System.Security.Cryptography;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>持久内容的冻结身份；摘要不能替代当前会话、Files 或发布资格。</summary>
internal sealed record AiKnowledgeCorrectionIdentity(Guid SourceId, Guid? RevisionId, int HeadVersion, string SourceHash, string PayloadHash);

/// <summary>只从完整验收的原解析及修订构造，供工作台和索引共享真实来源。</summary>
internal sealed record AiKnowledgeCorrectionSnapshot(AiKnowledgeCorrectionIdentity Identity, string ParserRuntimeId, AiKnowledgeCorrectionDocument Document)
{
    /// <summary>格式、文件摘要、大小及解析策略必须来自当前授权版本，不能从持久载荷反推。</summary>
    internal static AiKnowledgeCorrectionSnapshot? Verify(AiKnowledgeCorrectionSourceRecord source, AiKnowledgeCorrectionRevisionRecord? revision,
        AiKnowledgeParsedFormat format, string fileHash, int size, AiKnowledgeParsePolicy policy, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source.Id == Guid.Empty || source.VersionId == Guid.Empty || source.ActorUserId == Guid.Empty
            || source.FileHash != fileHash.ToLowerInvariant() || !Hash(source.InitialPayload, source.InitialPayloadHash)
            || !AiKnowledgeCorrectionWireCodec.ValidRuntime(source.ParserRuntimeId)) return null;
        var original = AiKnowledgeParsedWireCodec.Decode(source.OriginalPayload, format, fileHash, size, token, policy);
        if (original is null || AiKnowledgeCorrectionWireCodec.SourceHash(original, source.ParserRuntimeId, token) != source.SourceHash) return null;
        var initial = AiKnowledgeCorrectionWireCodec.Decode(source.InitialPayload, original, source.ParserRuntimeId, token);
        if (initial is null) return null;
        if (revision is null) return new(new(source.Id, null, 0, source.SourceHash, source.InitialPayloadHash), source.ParserRuntimeId, initial);
        if (revision.Id == Guid.Empty || revision.VersionId != source.VersionId || revision.SourceId != source.Id
            || revision.ActorUserId == Guid.Empty || revision.HeadVersion < 1 || revision.DocumentVersion < 2
            || revision.ParentId == Guid.Empty || !Hash(revision.Payload, revision.PayloadHash)) return null;
        var corrected = AiKnowledgeCorrectionWireCodec.Decode(revision.Payload, original, source.ParserRuntimeId, token);
        token.ThrowIfCancellationRequested();
        return corrected is null ? null : new(new(source.Id, revision.Id, revision.HeadVersion, source.SourceHash, revision.PayloadHash), source.ParserRuntimeId, corrected);
    }

    private static bool Hash(byte[] bytes, string expected) => bytes.Length is >= 2 and <= AiKnowledgeCorrectionWireCodec.MaxWireBytes
        && expected.Length == 64 && Convert.ToHexStringLower(SHA256.HashData(bytes)) == expected;
}
