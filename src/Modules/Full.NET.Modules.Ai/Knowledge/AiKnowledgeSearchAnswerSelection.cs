using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>冻结搜索选择的形状，实际授权与发布身份仍由来源 SQL 核证；禁止客户端正文参与生成。</summary>
internal static class AiKnowledgeSearchAnswerSelection
{
    internal static AiKnowledgeSearchAnswerScope? Freeze(AiKnowledgeSearchAnswerScope? scope, IReadOnlyList<Guid> bases, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (scope is null) return null;
        if (scope.EmbeddingModelVersion < 1 || scope.DocumentIds is null || scope.DocumentIds.Count > 8
            || scope.Sources is not { Count: >= 1 and <= 8 }) throw new InvalidDataException("搜索问答范围无效。");
        var documents = scope.DocumentIds.Count == 0 ? [] : AiKnowledgeRetrievalService.FreezeSelection(scope.DocumentIds, token);
        var sources = scope.Sources.ToArray();
        if (sources.Any(row => !AiKnowledgeSearchSources.Valid(row) || row is null || row.KnowledgeBaseId == Guid.Empty || row.DocumentId == Guid.Empty || row.VersionId == Guid.Empty
                || row.PublicationId == Guid.Empty || row.GenerationId == Guid.Empty || row.KnowledgeBaseVersion < 1 || row.DocumentVersion < 1
                || row.ManifestHash is not { Length: 64 } || row.ManifestHash.Any(character => !Uri.IsHexDigit(character))
                || !bases.Contains(row.KnowledgeBaseId) || documents.Length > 0 && !documents.Contains(row.DocumentId))
            || sources.Select(row => row.DocumentId).Distinct().Count() != sources.Length
            || bases.Any(id => !sources.Any(row => row.KnowledgeBaseId == id))
            || documents.Any(id => !sources.Any(row => row.DocumentId == id))) throw new InvalidDataException("搜索问答来源集合不完整。");
        return new(Array.AsReadOnly(documents), scope.EmbeddingModelVersion, Array.AsReadOnly(sources));
    }
}
