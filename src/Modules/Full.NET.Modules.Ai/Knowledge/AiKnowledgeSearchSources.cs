using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>搜索、对比、改写和冻结问答共用闭合来源协议，未知协议不能猜测为文件。</summary>
internal static class AiKnowledgeSearchSources
{
    internal static bool Valid(AiKnowledgeSearchSource? value) => value is not null
        && value.KnowledgeBaseId != Guid.Empty && value.DocumentId != Guid.Empty && value.VersionId != Guid.Empty
        && value.PublicationId != Guid.Empty && value.GenerationId != Guid.Empty && value.KnowledgeBaseVersion > 0 && value.DocumentVersion > 0
        && value.ManifestHash is { Length: 64 } && value.ManifestHash.All(char.IsAsciiHexDigit)
        && (value.SourceKind == "document-v1" && value.FaqRevisionId is null && value.FaqId is null
            || value.SourceKind == "faq-revision-v1" && value.FaqRevisionId is { } revision && revision != Guid.Empty && value.FaqId is { } faq && faq != Guid.Empty);

    internal static AiKnowledgeSearchSource Capture(AiKnowledgeIndexStatusRecord source, AiKnowledgeFaqAnswerSource? faq = null) => new(source.KnowledgeBaseId,
        source.KnowledgeBaseVersion, source.DocumentId, source.DocumentVersion, source.VersionId,
        source.PublicationId ?? throw new InvalidDataException(), source.TaskId ?? throw new InvalidDataException(),
        source.ActiveManifestHash ?? throw new InvalidDataException())
    { SourceKind = source.FaqRevisionId.HasValue ? "faq-revision-v1" : "document-v1", FaqRevisionId = source.FaqRevisionId,
        FaqId = source.FaqRevisionId.HasValue ? faq?.FaqId ?? throw new InvalidDataException() : null };

    internal static bool Matches(AiKnowledgeSearchSource expected, AiKnowledgeIndexStatusRecord actual) =>
        Valid(expected) && expected.KnowledgeBaseId == actual.KnowledgeBaseId && expected.KnowledgeBaseVersion == actual.KnowledgeBaseVersion
        && expected.DocumentId == actual.DocumentId && expected.DocumentVersion == actual.DocumentVersion && expected.VersionId == actual.VersionId
        && expected.PublicationId == actual.PublicationId && expected.GenerationId == actual.TaskId && expected.ManifestHash == actual.ActiveManifestHash
        && expected.FaqRevisionId == actual.FaqRevisionId;
}
