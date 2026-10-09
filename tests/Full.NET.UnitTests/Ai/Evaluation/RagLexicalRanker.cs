using System.Text;

namespace Full.NET.UnitTests.Ai.Evaluation;

/// <summary>固定的中英文分词与 BM25；只接收调用方已经按来源允许集过滤的正文，不执行指令。</summary>
internal static class RagLexicalRanker
{
    internal static IReadOnlyList<RagRetrievalRank> Rank(string question, IReadOnlyList<RagLexicalDocument> documents,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (documents is null || documents.Count > 4096) throw Invalid();
        var query = Tokens(question, 2048, token).Keys.ToArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
        var parsed = new List<(string Id, Dictionary<string, int> Terms, int Length)>(); long characters = 0;
        foreach (var document in documents)
        {
            token.ThrowIfCancellationRequested();
            if (document is null || string.IsNullOrWhiteSpace(document.ChunkId) || !seen.Add(document.ChunkId)) throw Invalid();
            var terms = Tokens(document.Text, 8000, token); characters += document.Text.Length;
            if (characters > 2 * 1024 * 1024) throw Invalid(); parsed.Add((document.ChunkId, terms, terms.Values.Sum()));
        }
        if (parsed.Count == 0 || query.Length == 0) return [];
        var average = parsed.Average(x => x.Length); if (average == 0) return [];
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var term in query)
        {
            var count = 0;
            foreach (var document in parsed) { token.ThrowIfCancellationRequested(); if (document.Terms.ContainsKey(term)) count++; }
            frequencies.Add(term, count);
        }
        var scores = new List<RagRetrievalRank>();
        foreach (var document in parsed)
        {
            double score = 0;
            foreach (var term in query)
            {
                token.ThrowIfCancellationRequested(); var count = document.Terms.GetValueOrDefault(term); if (count == 0) continue;
                var idf = Math.Log(1 + (parsed.Count - frequencies[term] + 0.5) / (frequencies[term] + 0.5));
                // 参数固定为 k1=1.2、b=0.75；长度统计来自同一允许候选集，不使用外部全局语料。
                score += idf * count * 2.2 / (count + 1.2 * (0.25 + 0.75 * document.Length / average));
            }
            if (!double.IsFinite(score)) throw Invalid(); if (score > 0) scores.Add(new(document.Id, score));
        }
        return scores.OrderByDescending(x => x.Score).ThenBy(x => x.ChunkId, StringComparer.Ordinal).ToArray();
    }

    private static Dictionary<string, int> Tokens(string text, int maximum, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum) throw Invalid();
        try { new UTF8Encoding(false, true).GetByteCount(text); } catch (EncoderFallbackException) { throw Invalid(); }
        var normalized = text.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        var terms = new Dictionary<string, int>(StringComparer.Ordinal); var ascii = new StringBuilder(); string? previous = null;
        void Add(string term) { if (term.Length > 0) terms[term] = terms.GetValueOrDefault(term) + 1; }
        void Flush()
        {
            var value = ascii.ToString().Trim('-', '_'); ascii.Clear(); Add(value);
            // 编号既保留完整形式，也索引组成部分；全角与大小写规范化后保持精确编号区分。
            if (value.Contains('-') || value.Contains('_')) foreach (var part in value.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)) Add(part);
        }
        foreach (var rune in normalized.EnumerateRunes())
        {
            token.ThrowIfCancellationRequested();
            if (rune.IsAscii && (Rune.IsLetterOrDigit(rune) || rune.Value is '-' or '_')) { ascii.Append((char)rune.Value); previous = null; continue; }
            Flush();
            if (!rune.IsAscii && Rune.IsLetterOrDigit(rune)) { var value = rune.ToString(); Add(value); if (previous is not null) Add(previous + value); previous = value; }
            else previous = null;
        }
        Flush(); return terms;
    }
    private static InvalidDataException Invalid() => new("invalid_retrieval_lexical_input");
}
