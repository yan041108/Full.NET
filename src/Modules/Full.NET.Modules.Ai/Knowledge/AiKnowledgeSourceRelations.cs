using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>只使用已验证完整清单中的图片父锚点；人工合并保留全部来源，补写不产生关系。</summary>
internal sealed record AiKnowledgeSourceRelations(string State, IReadOnlyList<AiKnowledgeSearchRelation> Relations,
    IReadOnlyList<AiKnowledgeRetrievalChunk> Blocks)
{
    internal const string Strategy = "embedded_image_parent_v1";
    internal static AiKnowledgeSourceRelations Select(IReadOnlyList<AiKnowledgeRetrievalChunk> chunks, int anchor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (chunks is null || chunks.Count is < 1 or > 256 || anchor < 0 || anchor >= chunks.Count
            || chunks.Any(chunk => chunk is null || chunk.Source is null)) throw Invalid();
        var ordered = chunks.OrderBy(chunk => chunk.Sequence).ToArray(); var source = ordered[0].Source; long total = 0;
        if (source.DocumentId == Guid.Empty || source.VersionId == Guid.Empty || source.TaskId is null || source.TaskId == Guid.Empty
            || source.PublicationId is null || source.PublicationId == Guid.Empty) throw Invalid();
        var locations = new AiKnowledgeSourceLocationResponse[ordered.Length][];
        var parents = new Dictionary<AiKnowledgeSourceLocationResponse, HashSet<int>>();
        var children = new Dictionary<AiKnowledgeSourceLocationResponse, HashSet<(int Sequence, AiKnowledgeSourceLocationResponse Location)>>();
        for (var index = 0; index < ordered.Length; index++)
        {
            token.ThrowIfCancellationRequested(); var chunk = ordered[index];
            if (chunk.Sequence != index || chunk.Source != source || chunk.Text is not { Length: >= 1 and <= 32768 }) throw Invalid();
            total += chunk.Text.Length; if (total > 4 * 1024 * 1024) throw Invalid();
            if (chunk.Correction is { } correction)
            {
                var captured = AiKnowledgeCorrectionCitations.Capture(correction)!;
                if (captured.TextLength != chunk.Text.Length || chunk.SourceLocation is not null) throw Invalid();
                locations[index] = captured.Spans.Where(span => span.SourceLocation is not null).Select(span => span.SourceLocation!).Distinct().ToArray();
            }
            else
            {
                if (!AiKnowledgeSourceLocations.Valid(chunk.SourceLocation)) throw Invalid();
                locations[index] = chunk.SourceLocation is { } location ? [location] : [];
            }
            foreach (var location in locations[index])
            {
                token.ThrowIfCancellationRequested();
                if (location.Image is { Parent: { } parent })
                {
                    if (parent.BlockSequence >= location.BlockSequence || parent.Image is not null) throw Invalid();
                    if (!children.TryGetValue(parent, out var values)) children[parent] = values = [];
                    values.Add((index, location));
                }
                else if (location.Image is null)
                {
                    if (!parents.TryGetValue(location, out var values)) parents[location] = values = [];
                    values.Add(index);
                }
            }
        }
        var roots = locations[anchor].SelectMany(location => location.Image?.Parent is { } parent
            ? new[] { parent } : children.ContainsKey(location) ? new[] { location } : []).Distinct().ToArray();
        var selected = new HashSet<int> { anchor };
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            if (parents.TryGetValue(root, out var parentRows)) selected.UnionWith(parentRows);
            if (children.TryGetValue(root, out var childRows)) selected.UnionWith(childRows.Select(child => child.Sequence));
            if (selected.Count > 5) throw Invalid();
        }
        if (selected.Sum(index => ordered[index].Text.Length) > 32768) throw Invalid();
        var edges = new HashSet<AiKnowledgeSearchRelation>();
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            if (!children.TryGetValue(root, out var childRows)) continue;
            var parentRows = parents.TryGetValue(root, out var found) ? found.Select(index => (int?)index).ToArray() : new int?[] { null };
            foreach (var parent in parentRows)
            foreach (var child in childRows)
            {
                token.ThrowIfCancellationRequested(); edges.Add(new(parent, child.Sequence, root, child.Location));
                if (edges.Count > 128) throw Invalid();
            }
        }
        var frozen = edges.OrderBy(edge => edge.ParentLocation.BlockSequence).ThenBy(edge => edge.ChildLocation.BlockSequence)
            .ThenBy(edge => edge.ParentSequence).ThenBy(edge => edge.ChildSequence).ToArray();
        var blocks = selected.Order().Select(index => ordered[index] with
            { Vector = ReadOnlyMemory<float>.Empty, Correction = AiKnowledgeCorrectionCitations.Capture(ordered[index].Correction) }).ToArray();
        return new(frozen.Length == 0 ? "none" : frozen.Any(edge => edge.ParentSequence is null) ? "parent_not_indexed" : "complete",
            Array.AsReadOnly(frozen), Array.AsReadOnly(blocks));
    }
    private static InvalidDataException Invalid() => new("真实来源关系或独立补全预算无效。");
}
