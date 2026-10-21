using System.Security.Cryptography;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>只复用纯内容证明；会话、租约、模型与账本资格仍须在每个权威边界重新检查。</summary>
internal sealed class AiKnowledgeIndexPdfVerifiedManifest
{
    internal AiKnowledgeIndexPdfManifest Manifest { get; }
    internal string PayloadHash { get; }

    private AiKnowledgeIndexPdfVerifiedManifest(AiKnowledgeIndexPdfManifest manifest, string hash)
    {
        Manifest = manifest;
        PayloadHash = hash;
    }

    internal static AiKnowledgeIndexPdfVerifiedManifest Capture(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 8 or > AiKnowledgeIndexPdfManifestCodec.MaxPayloadBytes)
            throw new InvalidDataException("Invalid PDF manifest.");
        var owned = bytes.ToArray();
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(owned));
            var manifest = AiKnowledgeIndexPdfManifestCodec.Decode(owned, token);
            token.ThrowIfCancellationRequested();
            return new(manifest, hash);
        }
        finally { CryptographicOperations.ZeroMemory(owned); }
    }
}
