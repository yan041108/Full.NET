namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>从已授权完整代次中选择连续窗口；没有父子关系证明时不猜测结构关系。</summary>
internal sealed record AiKnowledgeContextWindow(IReadOnlyList<AiKnowledgeRetrievalChunk> Blocks, bool HasPrevious, bool HasNext)
{
    internal const string Strategy = "adjacent_sequence_v1";
    internal static AiKnowledgeContextWindow Select(IReadOnlyList<AiKnowledgeRetrievalChunk> chunks, int anchor, int radius, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (chunks is null || chunks.Count is < 1 or > 256 || anchor < 0 || radius is < 0 or > 2
            || chunks.Any(chunk => chunk is null || chunk.Source is null)) throw Invalid();
        var ordered = chunks.OrderBy(chunk => chunk.Sequence).ToArray(); long total = 0;
        var first = ordered[0].Source;
        if (first.DocumentId == Guid.Empty || first.VersionId == Guid.Empty || first.TaskId is null || first.TaskId == Guid.Empty
            || first.PublicationId is null || first.PublicationId == Guid.Empty) throw Invalid();
        for (var i = 0; i < ordered.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var chunk = ordered[i];
            if (chunk.Sequence != i || chunk.Source.DocumentId != first.DocumentId || chunk.Source.TaskId != first.TaskId
                || chunk.Source.VersionId != first.VersionId || chunk.Source.PublicationId != first.PublicationId
                || chunk.Text is not { Length: >= 1 and <= 32768 }) throw Invalid();
            total += chunk.Text.Length; if (total > 4 * 1024 * 1024) throw Invalid();
        }
        if (anchor >= ordered.Length) throw Invalid();
        var start = Math.Max(0, anchor - radius); var end = Math.Min(ordered.Length - 1, anchor + radius);
        var selected = new List<AiKnowledgeRetrievalChunk>(); var size = 0;
        for (var i = start; i <= end; i++)
        {
            token.ThrowIfCancellationRequested(); size += ordered[i].Text.Length; if (size > 32768) throw Invalid();
            selected.Add(ordered[i] with { Vector = ReadOnlyMemory<float>.Empty, Correction = AiKnowledgeCorrectionCitations.Capture(ordered[i].Correction) });
        }
        return new(selected.AsReadOnly(), start > 0, end < ordered.Length - 1);
    }
    private static InvalidDataException Invalid() => new("完整上下文或窗口超出支持范围。");
}
