using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Full.NET.UnitTests.Ai.Evaluation;

/// <summary>三种闭合检索策略只在冻结语料与当前案例允许集内计算；无生产读取或模型派发能力。</summary>
internal static partial class RagRetrievalExecution
{
    internal const string PolicyVersion = "rag-retrieval-execution-v1";
    private const int RerankWindow = 20;
    internal static RagRetrievalExecutionReport Execute(RagEvaluationCorpus corpus, string digest,
        RagRetrievalExecutionInput input, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        Require(corpus is not null && corpus.Chunks is { Length: > 0 and <= 4096 } && corpus.Cases is { Length: > 0 and <= 256 }
            && corpus.Policy?.K == 5, "invalid_retrieval_corpus");
        RagEvaluationScorer.ValidateCorpus(corpus);
        long characters = 0;
        foreach (var chunk in corpus.Chunks) { token.ThrowIfCancellationRequested(); Require(chunk.Text.Length <= 8000, "retrieval_text_budget"); characters += chunk.Text.Length; }
        Require(characters <= 2 * 1024 * 1024 && corpus.Cases.All(x => x.Question.Length <= 2048), "retrieval_text_budget");
        Require(input is not null && input.SchemaVersion == 1 && input.DatasetVersion == corpus.DatasetVersion && input.DatasetSha256 == digest
            && HashKey().IsMatch(digest) && ModelKey(input.ModelKey) && input.Vectors is not null && input.Queries is not null
            && input.Vectors.Length == corpus.Chunks.Length && input.Queries.Length == corpus.Cases.Length
            && (input.EmbeddingEvidenceKind == "synthetic_features" ? input.Dimensions == 128 && input.ModelKey.StartsWith("synthetic-", StringComparison.Ordinal)
                : input.EmbeddingEvidenceKind == "provided_model" && input.Dimensions is 1536 or 3072 && !input.ModelKey.StartsWith("synthetic-", StringComparison.Ordinal))
            && (input.RerankModelKey is null || ModelKey(input.RerankModelKey)), "invalid_retrieval_execution_input");
        Require((long)input.Dimensions * input.Vectors.Length * sizeof(float) <= 32 * 1024 * 1024, "retrieval_vector_budget");
        var vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var row in input.Vectors)
        {
            token.ThrowIfCancellationRequested(); Require(row is not null && row.ModelKey == input.ModelKey
                && !string.IsNullOrWhiteSpace(row.ChunkId) && vectors.TryAdd(row.ChunkId, row.Values), "invalid_retrieval_vector_binding");
            Norm(row.Values, input.Dimensions, token);
        }
        Require(vectors.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(corpus.Chunks.Select(x => x.ChunkId)), "invalid_retrieval_vector_binding");
        var queries = new Dictionary<string, RagRetrievalQuery>(StringComparer.Ordinal);
        foreach (var query in input.Queries)
        {
            token.ThrowIfCancellationRequested(); Require(query is not null && query.ModelKey == input.ModelKey
                && !string.IsNullOrWhiteSpace(query.CaseId) && queries.TryAdd(query.CaseId, query), "invalid_retrieval_query_binding");
            Norm(query.Values, input.Dimensions, token);
        }
        Require(queries.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(corpus.Cases.Select(x => x.CaseId)), "invalid_retrieval_query_binding");
        var basic = new List<RagRetrievalCaseExecution>(); var hybrid = new List<RagRetrievalCaseExecution>(); var rerank = new List<RagRetrievalCaseExecution>();
        foreach (var testCase in corpus.Cases)
        {
            token.ThrowIfCancellationRequested(); var query = queries[testCase.CaseId];
            // 词频与候选窗口都在过滤后建立，禁止未获允许的片段影响 IDF、融合或重排排名。
            var eligible = corpus.Chunks.Where(x => x.TenantScope == testCase.TenantScope
                && testCase.AllowedSourceVersions.Contains(x.SourceVersion, StringComparer.Ordinal)).ToArray();
            var start = Stopwatch.GetTimestamp();
            var dense = eligible.Select(x => new RagRetrievalRank(x.ChunkId, Cosine(query.Values, vectors[x.ChunkId], token)))
                .OrderByDescending(x => x.Score).ThenBy(x => x.ChunkId, StringComparer.Ordinal).ToArray();
            basic.Add(Result(testCase, "computed", eligible.Length, dense, [], Stopwatch.GetElapsedTime(start).TotalMilliseconds));
            start = Stopwatch.GetTimestamp();
            var lexical = RagLexicalRanker.Rank(testCase.Question, eligible.Select(x => new RagLexicalDocument(x.ChunkId, x.Text)).ToArray(), token);
            var fused = Fuse(dense, lexical, token); var window = fused.Take(RerankWindow).Select(x => x.ChunkId).ToArray();
            hybrid.Add(Result(testCase, "computed", eligible.Length, fused, window, Stopwatch.GetElapsedTime(start).TotalMilliseconds));
            if (query.RerankScores is null)
            {
                rerank.Add(Result(testCase, "not_measured", eligible.Length, [], window, 0)); continue;
            }
            start = Stopwatch.GetTimestamp(); Require(input.RerankModelKey is not null && query.RerankScores.Length == window.Length, "invalid_retrieval_rerank_scores");
            var scores = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var score in query.RerankScores)
            {
                token.ThrowIfCancellationRequested(); Require(score is not null && !string.IsNullOrWhiteSpace(score.ChunkId)
                    && double.IsFinite(score.Score) && scores.TryAdd(score.ChunkId, score.Score), "invalid_retrieval_rerank_scores");
            }
            Require(scores.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(window), "invalid_retrieval_rerank_scores");
            var reordered = window.Select((id, position) => (Rank: new RagRetrievalRank(id, scores[id]), Position: position))
                .OrderByDescending(x => x.Rank.Score).ThenBy(x => x.Position).Select(x => x.Rank).ToArray();
            rerank.Add(Result(testCase, "computed_supplied_scores", eligible.Length, reordered, window, Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        }
        token.ThrowIfCancellationRequested();
        var inputDigest = RagEvaluationJson.Digest(JsonSerializer.Serialize(input, RagEvaluationJson.Options));
        // 序列化大向量期间仍可能收到取消，摘要完成后必须再次复核才交付完整报告。
        token.ThrowIfCancellationRequested();
        return new(1, PolicyVersion, corpus.DatasetVersion, digest, inputDigest,
            input.EmbeddingEvidenceKind, input.ModelKey, input.RerankModelKey, true,
            [Strategy("basic", "exact-cosine-v1", basic), Strategy("keyword_vector", "bm25-rrf-v1", hybrid), Strategy("rerank", "supplied-scores-v1", rerank)]);
    }

    internal static RagRetrievalRank[] Fuse(IReadOnlyList<RagRetrievalRank> dense, IReadOnlyList<RagRetrievalRank> lexical, CancellationToken token = default)
    {
        // RRF 的两个排名从一开始计数；无词法命中时仍保留向量排名，不给零命中文档虚构词法票数。
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var list in new[] { dense, lexical })
            for (var index = 0; index < list.Count; index++) { token.ThrowIfCancellationRequested(); scores[list[index].ChunkId] = scores.GetValueOrDefault(list[index].ChunkId) + 1d / (60 + index + 1); }
        return scores.Select(x => new RagRetrievalRank(x.Key, x.Value)).OrderByDescending(x => x.Score).ThenBy(x => x.ChunkId, StringComparer.Ordinal).ToArray();
    }

    private static RagRetrievalCaseExecution Result(RagEvaluationCase testCase, string status, int count, RagRetrievalRank[] ranking, string[] window, double duration)
    {
        var top = ranking.Take(5).ToArray();
        double? recall = status == "not_measured" || testCase.ShouldRefuse ? null
            : (double)testCase.ExpectedChunkIds.Count(id => top.Any(x => x.ChunkId == id)) / testCase.ExpectedChunkIds.Length;
        return new(testCase.CaseId, status, count, top, window, recall, duration);
    }
    private static RagRetrievalStrategyExecution Strategy(string key, string version, List<RagRetrievalCaseExecution> cases)
        => new(key, version, cases.Any(x => x.Status == "not_measured") ? null : cases.Where(x => x.RecallAt5.HasValue).Select(x => x.RecallAt5).DefaultIfEmpty(null).Average(), cases.ToArray());
    private static double Cosine(float[] query, float[] values, CancellationToken token)
    {
        var left = Norm(query, query.Length, token); var right = Norm(values, query.Length, token); double dot = 0;
        for (var i = 0; i < query.Length; i++) { token.ThrowIfCancellationRequested(); dot += (double)query[i] * values[i]; }
        var score = dot / Math.Sqrt(left) / Math.Sqrt(right); Require(double.IsFinite(score), "invalid_retrieval_vector"); return Math.Clamp(score, -1, 1);
    }
    private static double Norm(float[] values, int dimensions, CancellationToken token)
    {
        Require(values is not null && values.Length == dimensions, "invalid_retrieval_vector"); double sum = 0;
        foreach (var value in values) { token.ThrowIfCancellationRequested(); Require(float.IsFinite(value), "invalid_retrieval_vector"); sum += (double)value * value; }
        Require(double.IsFinite(sum) && sum > 0, "invalid_retrieval_vector"); return sum;
    }
    private static bool ModelKey(string key) => key is not null && MachineKey().IsMatch(key) && !key.StartsWith("none-", StringComparison.Ordinal) && !key.StartsWith("reference-", StringComparison.Ordinal);
    private static void Require([DoesNotReturnIf(false)] bool valid, string code) { if (!valid) throw new InvalidDataException(code); }
    [GeneratedRegex(@"\A[a-z0-9][a-z0-9._:-]{0,127}\z", RegexOptions.CultureInvariant)] private static partial Regex MachineKey();
    [GeneratedRegex(@"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant)] private static partial Regex HashKey();
}
