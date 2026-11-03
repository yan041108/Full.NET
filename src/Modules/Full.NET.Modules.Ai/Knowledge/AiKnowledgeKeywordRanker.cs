using System.Buffers;
using System.Text;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>完整授权候选上的字面全部词匹配；不调用模型，不将词频分数冒充语义质量。</summary>
internal static class AiKnowledgeKeywordRanker
{
    internal const string Strategy = "keyword_all_v1";
    internal static string[] Terms(string query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (query is null || query.Length is < 1 or > 512) throw Invalid();
        var remaining = query.AsSpan();
        while (!remaining.IsEmpty)
        {
            token.ThrowIfCancellationRequested();
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done) throw Invalid();
            remaining = remaining[consumed..];
        }
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length is < 1 or > 16 || terms.Any(term => term.Length > 64 || term.Any(char.IsControl))) throw Invalid();
        return terms.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static IReadOnlyList<AiKnowledgeRankedChunk> Rank(string query, IReadOnlyList<AiKnowledgeRetrievalChunk> chunks, CancellationToken token)
    {
        var terms = Terms(query, token); ArgumentNullException.ThrowIfNull(chunks);
        var count = chunks.Count; if (count > 256) throw Invalid();
        var frozen = new AiKnowledgeRetrievalChunk[count];
        for (var i = 0; i < count; i++) { token.ThrowIfCancellationRequested(); frozen[i] = chunks[i]; }
        var seen = new HashSet<(Guid, int)>(); var result = new List<AiKnowledgeRankedChunk>(); long textLength = 0;
        foreach (var chunk in frozen)
        {
            token.ThrowIfCancellationRequested();
            if (chunk is null || chunk.Source is null || chunk.Source.TaskId is not { } task || task == Guid.Empty
                || chunk.Source.DocumentId == Guid.Empty || chunk.Sequence < 0 || !seen.Add((task, chunk.Sequence))
                || chunk.Text is null || chunk.Text.Length is < 1 or > 32768) throw Invalid();
            textLength += chunk.Text.Length; if (textLength > 4 * 1024 * 1024) throw Invalid();
            var score = 0; var matched = true;
            foreach (var term in terms)
            {
                var offset = 0; var occurrences = 0;
                while (offset <= chunk.Text.Length - term.Length && occurrences < 32)
                {
                    token.ThrowIfCancellationRequested();
                    var found = chunk.Text.IndexOf(term, offset, StringComparison.OrdinalIgnoreCase);
                    if (found < 0) break;
                    occurrences++; offset = found + term.Length;
                }
                if (occurrences == 0) { matched = false; break; }
                score += occurrences;
            }
            if (matched) result.Add(new(chunk, score));
        }
        return Array.AsReadOnly(result.OrderByDescending(row => row.Score)
            .ThenBy(row => row.Chunk.Source.DocumentId.ToString("N"), StringComparer.Ordinal).ThenBy(row => row.Chunk.Sequence).ToArray());
    }
    private static InvalidDataException Invalid() => new("关键词或完整候选超出受支持的有界字面搜索规则。");
}
