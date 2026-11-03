using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>只从已验证的完整人工计划投影引用；模型与客户端不能构造原文锚点。</summary>
internal static class AiKnowledgeCorrectionCitations
{
    internal static AiKnowledgeCorrectionCitationResponse Create(AiKnowledgeCorrectionIndexPlan plan, AiKnowledgeCorrectionIndexChunk chunk, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var document = plan.Snapshot.Document;
        if (chunk.Sequence < 0 || chunk.Sequence >= plan.Chunks.Count || plan.Chunks[chunk.Sequence] != chunk) throw Invalid();
        var parts = document.Blocks.SelectMany(b => b.Parts).ToDictionary(p => p.Id);
        var anchors = document.Anchors.ToDictionary(a => a.Id); var spans = new List<AiKnowledgeCorrectionSpanResponse>();
        foreach (var reference in chunk.References)
        {
            token.ThrowIfCancellationRequested(); var part = parts[reference.PartId];
            var block = part.AnchorId is { } id ? anchors[id].BlockSequence : (int?)null;
            var location = reference.Position is { } position && block is { } sequence
                ? AiKnowledgeSourceLocations.Create(sequence, position, document.Source) : null;
            spans.Add(new(reference.Origin switch { AiKnowledgeCorrectionOrigin.Original => "original", AiKnowledgeCorrectionOrigin.Corrected => "corrected", AiKnowledgeCorrectionOrigin.Supplement => "supplement", _ => throw Invalid() },
                reference.ChunkOffset, reference.Length, location, reference.Position?.Offset, reference.Position?.Length));
        }
        var result = new AiKnowledgeCorrectionCitationResponse(plan.Snapshot.Identity.RevisionId!.Value, chunk.Text.Length, spans.AsReadOnly());
        if (!Valid(result)) throw Invalid(); return result;
    }

    /// <summary>校验持久化或上下文的有界结构；人工核对允许保留低置信度原 OCR，但不能抬高其置信度。</summary>
    internal static bool Valid(AiKnowledgeCorrectionCitationResponse? value)
    {
        if (value is null || value.RevisionId == Guid.Empty || value.TextLength is < 1 or > 8000 || value.Spans is null) return false;
        var count = value.Spans.Count; if (count is < 1 or > 32) return false; var end = 0;
        for (var index = 0; index < count; index++)
        {
            var span = value.Spans[index];
            if (span is null || span.TextOffset < end || span.TextLength <= 0 || span.TextOffset > value.TextLength - span.TextLength) return false;
            end = span.TextOffset + span.TextLength;
            if (span.ContentKind == "supplement")
            { if (span.SourceLocation is not null || span.SourceOffset is not null || span.SourceLength is not null) return false; }
            else if (span.ContentKind is not ("original" or "corrected") || span.SourceLocation is null
                || !AiKnowledgeSourceLocations.Valid(span.SourceLocation, reviewed: span.ContentKind == "corrected")
                || span.SourceOffset is not (>= 0) || span.SourceLength is not (> 0) || (long)span.SourceOffset + span.SourceLength > int.MaxValue
                || span.ContentKind == "original" && span.SourceLength != span.TextLength) return false;
        }
        return true;
    }
    /// <summary>不与供应集合共享可写存储，避免上下文冻结后引用性质被替换。</summary>
    internal static AiKnowledgeCorrectionCitationResponse? Capture(AiKnowledgeCorrectionCitationResponse? value)
    {
        if (value is null) return null; if (value.Spans is null) throw Invalid();
        var count = value.Spans.Count; if (count is < 1 or > 32) throw Invalid(); var owned = new AiKnowledgeCorrectionSpanResponse[count];
        for (var index = 0; index < count; index++) owned[index] = value.Spans[index];
        var result = value with { Spans = Array.AsReadOnly(owned) }; if (!Valid(result)) throw Invalid(); return result;
    }
    private static InvalidDataException Invalid() => new("Invalid human correction citation.");
}
