using Full.NET.UnitTests.Ai.Evaluation;
using Full.NET.AiRetrieval.Probe;
using System.Text.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace Full.NET.UnitTests.Ai;

/// <summary>检索计算真实执行，但合成向量和受控分数不代表模型或生产授权已验收。</summary>
[TestClass]
public sealed class RagRetrievalExecutionTests
{
    public TestContext TestContext { get; set; } = null!;
    internal static RagRetrievalExecutionInput Input()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus();
        return new(1, corpus.DatasetVersion, digest, "synthetic_features", "synthetic-featurehash-v1", 128, null,
            corpus.Chunks.Select(x => new RagRetrievalVector(x.ChunkId, "synthetic-featurehash-v1", ProbeVectorSearch.Embed(x.Text))).ToArray(),
            corpus.Cases.Select(x => new RagRetrievalQuery(x.CaseId, "synthetic-featurehash-v1", ProbeVectorSearch.Embed(x.Question), null)).ToArray());
    }

    [TestMethod]
    public void Frozen_corpus_executes_basic_and_hybrid_but_missing_rerank_is_not_measured()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var report = RagRetrievalExecution.Execute(corpus, digest, Input());
        CollectionAssert.AreEqual(new[] { "basic", "keyword_vector", "rerank" }, report.Strategies.Select(x => x.StrategyKey).ToArray());
        Assert.IsTrue(report.Strategies.Take(2).All(x => x.Cases.All(y => y.Status == "computed")));
        Assert.IsTrue(report.Strategies[2].Cases.All(x => x.Status == "not_measured" && x.RecallAt5 is null));
        Assert.IsTrue(report.RetrievalOnly);
    }

    [TestMethod]
    public void Chinese_terms_and_exact_identifiers_rank_matches_before_irrelevant_text()
    {
        var ranked = RagLexicalRanker.Rank("处理工单 FN-DEMO-042", [new("wrong", "天气晴朗"), new("correct", "工单 FN-DEMO-042 必须复核。")]);
        Assert.AreEqual("correct", ranked[0].ChunkId); Assert.IsTrue(ranked[0].Score > 0);
        Assert.IsFalse(ranked.Any(x => x.ChunkId == "wrong"));
    }

    [TestMethod]
    public void Bm25_matches_fixed_formula_and_ignores_repeated_query_terms()
    {
        RagLexicalDocument[] documents = [new("a", "cat cat"), new("b", "dog"), new("c", "cat")];
        var ranked = RagLexicalRanker.Rank("cat", documents); var repeated = RagLexicalRanker.Rank("cat cat", documents);
        Assert.AreEqual(Math.Log(1.6) * 4.4 / 3.65, ranked[0].Score, 1e-12);
        Assert.AreEqual("a", ranked[0].ChunkId); CollectionAssert.AreEqual(ranked.ToArray(), repeated.ToArray());
    }

    [TestMethod]
    public void Normalized_identifiers_and_stable_ties_do_not_depend_on_input_order()
    {
        RagLexicalDocument[] documents = [new("b", "FN-DEMO-042 工单"), new("a", "FN-DEMO-042 工单")];
        CollectionAssert.AreEqual(new[] { "a", "b" }, RagLexicalRanker.Rank("ｆｎ－ｄｅｍｏ－０４２", documents).Select(x => x.ChunkId).ToArray());
    }

    [TestMethod]
    public void Fusion_uses_reciprocal_ranks_and_zero_keyword_hits_preserve_dense_order()
    {
        RagRetrievalRank[] dense = [new("b", 0.9), new("a", 0.8)];
        var tied = RagRetrievalExecution.Fuse(dense, [new("a", 10), new("b", 1)]);
        Assert.AreEqual(1d / 61 + 1d / 62, tied[0].Score, 1e-12); Assert.AreEqual("a", tied[0].ChunkId);
        CollectionAssert.AreEqual(new[] { "b", "a" }, RagRetrievalExecution.Fuse(dense, []).Select(x => x.ChunkId).ToArray());
    }

    [TestMethod]
    public void Candidate_rankings_never_include_wrong_scope_or_disallowed_sources()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var report = RagRetrievalExecution.Execute(corpus, digest, Input());
        foreach (var strategy in report.Strategies.Take(2)) foreach (var row in strategy.Cases)
        {
            var testCase = corpus.Cases.Single(x => x.CaseId == row.CaseId);
            var allowed = corpus.Chunks.Where(x => x.TenantScope == testCase.TenantScope && testCase.AllowedSourceVersions.Contains(x.SourceVersion)).Select(x => x.ChunkId).ToArray();
            Assert.AreEqual(allowed.Length, row.EligibleCandidateCount);
            Assert.IsTrue(row.TopChunks.All(x => allowed.Contains(x.ChunkId))); Assert.IsTrue(row.RerankWindowChunkIds.All(allowed.Contains));
        }
    }

    [TestMethod]
    public void Disallowed_text_cannot_change_bm25_document_frequencies_or_hybrid_ranking()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var input = Input(); var testCase = corpus.Cases.First(x => !x.ShouldRefuse);
        var before = RagRetrievalExecution.Execute(corpus, digest, input);
        var changed = corpus with { Chunks = corpus.Chunks.Select(x => !testCase.AllowedSourceVersions.Contains(x.SourceVersion) || x.TenantScope != testCase.TenantScope
            ? x with { Text = testCase.Question } : x).ToArray() };
        var after = RagRetrievalExecution.Execute(changed, digest, input);
        CollectionAssert.AreEqual(before.Strategies[1].Cases.Single(x => x.CaseId == testCase.CaseId).TopChunks,
            after.Strategies[1].Cases.Single(x => x.CaseId == testCase.CaseId).TopChunks);
    }

    private static RagRetrievalExecutionInput WithRerank(RagEvaluationCorpus corpus, string digest, RagRetrievalExecutionInput input)
    {
        var report = RagRetrievalExecution.Execute(corpus, digest, input);
        return input with { RerankModelKey = "controlled-reranker-v1", Queries = input.Queries.Select(query => query with {
            RerankScores = report.Strategies[1].Cases.Single(x => x.CaseId == query.CaseId).RerankWindowChunkIds
                .Select((id, index) => new RagRerankScore(id, index)).ToArray() }).ToArray() };
    }

    [TestMethod]
    public void Supplied_rerank_scores_order_only_complete_hybrid_window()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var input = WithRerank(corpus, digest, Input());
        var report = RagRetrievalExecution.Execute(corpus, digest, input);
        foreach (var row in report.Strategies[2].Cases)
        {
            Assert.AreEqual("computed_supplied_scores", row.Status);
            CollectionAssert.AreEqual(row.RerankWindowChunkIds.Reverse().Take(5).ToArray(), row.TopChunks.Select(x => x.ChunkId).ToArray());
        }
    }

    [TestMethod]
    [DataRow("model")]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("outside")]
    [DataRow("nan")]
    public void Rerank_evidence_cannot_inject_omit_or_mix_candidates(string mutation)
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var input = WithRerank(corpus, digest, Input());
        var at = Array.FindIndex(input.Queries, x => x.RerankScores!.Length > 0); var query = input.Queries[at]; var scores = query.RerankScores!;
        if (mutation == "model") input = input with { RerankModelKey = null };
        else { query = query with { RerankScores = mutation switch { "missing" => scores[..^1], "duplicate" => [.. scores, scores[0]],
            "outside" => [scores[0] with { ChunkId = "unallowed" }, .. scores[1..]], _ => [scores[0] with { Score = double.NaN }, .. scores[1..]] } };
            input.Queries[at] = query; }
        Assert.ThrowsExactly<InvalidDataException>(() => RagRetrievalExecution.Execute(corpus, digest, input));
    }

    [TestMethod]
    public void Partial_rerank_is_not_reported_as_complete_recall()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var input = WithRerank(corpus, digest, Input());
        input.Queries[0] = input.Queries[0] with { RerankScores = null };
        Assert.IsNull(RagRetrievalExecution.Execute(corpus, digest, input).Strategies[2].RecallAt5);
    }

    [TestMethod]
    [DataRow("version")]
    [DataRow("digest")]
    [DataRow("kind")]
    [DataRow("model")]
    [DataRow("dimensions")]
    [DataRow("missing-vector")]
    [DataRow("duplicate-vector")]
    [DataRow("vector-model")]
    [DataRow("missing-query")]
    [DataRow("duplicate-query")]
    [DataRow("query-model")]
    public void Execution_rejects_mixed_or_incomplete_input(string mutation)
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var input = Input();
        input = mutation switch {
            "version" => input with { DatasetVersion = "changed" }, "digest" => input with { DatasetSha256 = new('a', 64) },
            "kind" => input with { EmbeddingEvidenceKind = "real" }, "model" => input with { ModelKey = "none-reference" }, "dimensions" => input with { Dimensions = 2 },
            "missing-vector" => input with { Vectors = input.Vectors[..^1] }, "duplicate-vector" => input with { Vectors = [.. input.Vectors[..^1], input.Vectors[0]] },
            "vector-model" => input with { Vectors = [input.Vectors[0] with { ModelKey = "other" }, .. input.Vectors[1..]] },
            "missing-query" => input with { Queries = input.Queries[..^1] }, "duplicate-query" => input with { Queries = [.. input.Queries[..^1], input.Queries[0]] },
            _ => input with { Queries = [input.Queries[0] with { ModelKey = "other" }, .. input.Queries[1..]] } };
        Assert.ThrowsExactly<InvalidDataException>(() => RagRetrievalExecution.Execute(corpus, digest, input));
    }

    [TestMethod]
    [DataRow("zero")]
    [DataRow("nan")]
    [DataRow("infinity")]
    [DataRow("length")]
    public void Invalid_vectors_fail_before_any_report(string mutation)
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var input = Input(); var values = new float[128]; values[0] = 1;
        if (mutation == "zero") values[0] = 0; if (mutation == "nan") values[0] = float.NaN;
        if (mutation == "infinity") values[0] = float.PositiveInfinity; if (mutation == "length") values = [1];
        input.Vectors[0] = input.Vectors[0] with { Values = values };
        Assert.ThrowsExactly<InvalidDataException>(() => RagRetrievalExecution.Execute(corpus, digest, input));
    }

    [TestMethod]
    [DataRow(1536)]
    [DataRow(3072)]
    public void Extreme_finite_provided_model_vectors_use_double_accumulation(int dimensions)
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var input = Input(); var vector = Enumerable.Repeat(float.MaxValue, dimensions).ToArray();
        input = input with { EmbeddingEvidenceKind = "provided_model", ModelKey = "controlled-embedding-v1", Dimensions = dimensions,
            Vectors = input.Vectors.Select(x => x with { ModelKey = "controlled-embedding-v1", Values = vector }).ToArray(),
            Queries = input.Queries.Select(x => x with { ModelKey = "controlled-embedding-v1", Values = vector }).ToArray() };
        Assert.IsTrue(RagRetrievalExecution.Execute(corpus, digest, input).Strategies[0].Cases.SelectMany(x => x.TopChunks).All(x => Math.Abs(x.Score - 1) < 1e-12));
    }

    [TestMethod]
    public void Cancellation_is_observed_by_each_stage_without_partial_result()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel(); var (corpus, digest) = RagEvaluationJson.LoadCorpus();
        Assert.ThrowsExactly<OperationCanceledException>(() => RagRetrievalExecution.Execute(corpus, digest, Input(), stop.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => RagLexicalRanker.Rank("query", [], stop.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => RagRetrievalExecution.Fuse([new("a", 1)], [], stop.Token));
    }

    [TestMethod]
    public void Execution_does_not_mutate_inputs_and_report_does_not_copy_source_text()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var input = Input(); var before = JsonSerializer.Serialize(input, RagEvaluationJson.Options);
        var report = RagRetrievalExecution.Execute(corpus, digest, input); Assert.AreEqual(before, JsonSerializer.Serialize(input, RagEvaluationJson.Options));
        var json = JsonSerializer.Serialize(report, RagEvaluationJson.Options);
        using var document = JsonDocument.Parse(json);
        var strings = Strings(document.RootElement).ToArray();
        foreach (var text in corpus.Chunks.Select(x => x.Text).Concat(corpus.Cases.Select(x => x.Question))) Assert.IsFalse(strings.Contains(text));
    }

    private static IEnumerable<string> Strings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) yield return element.GetString()!;
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) foreach (var value in Strings(property.Value)) yield return value;
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) foreach (var value in Strings(child)) yield return value;
    }

    [TestMethod]
    public void Rerank_window_is_capped_at_twenty_and_equal_scores_preserve_hybrid_order()
    {
        var (frozen, _) = RagEvaluationJson.LoadCorpus(); var sample = frozen.Cases.First(x => !x.ShouldRefuse);
        var chunk = frozen.Chunks.Single(x => x.ChunkId == sample.ExpectedChunkIds[0]);
        var corpus = frozen with { Chunks = Enumerable.Range(0, 21).Select(i => chunk with { ChunkId = "candidate-" + i.ToString("D2") }).ToArray(),
            Cases = [sample with { ExpectedChunkIds = ["candidate-00"] }] };
        var digest = RagEvaluationJson.Digest(JsonSerializer.Serialize(corpus, RagEvaluationJson.Options));
        var input = Input() with { DatasetSha256 = digest,
            Vectors = corpus.Chunks.Select(x => new RagRetrievalVector(x.ChunkId, "synthetic-featurehash-v1", ProbeVectorSearch.Embed(x.Text))).ToArray(),
            Queries = [new(sample.CaseId, "synthetic-featurehash-v1", ProbeVectorSearch.Embed(sample.Question), null)] };
        var scored = WithRerank(corpus, digest, input); scored.Queries[0] = scored.Queries[0] with {
            RerankScores = scored.Queries[0].RerankScores!.Select(x => x with { Score = 1 }).ToArray() };
        var report = RagRetrievalExecution.Execute(corpus, digest, scored);
        Assert.AreEqual(21, report.Strategies[2].Cases[0].EligibleCandidateCount);
        Assert.AreEqual(20, report.Strategies[2].Cases[0].RerankWindowChunkIds.Length);
        CollectionAssert.AreEqual(report.Strategies[1].Cases[0].TopChunks.Select(x => x.ChunkId).ToArray(), report.Strategies[2].Cases[0].TopChunks.Select(x => x.ChunkId).ToArray());
        scored.Queries[0].RerankScores![0] = new("candidate-20", double.MaxValue);
        Assert.ThrowsExactly<InvalidDataException>(() => RagRetrievalExecution.Execute(corpus, digest, scored));
    }

    [TestMethod]
    [DataRow("question")]
    [DataRow("text")]
    [DataRow("unicode")]
    [DataRow("duplicate")]
    [DataRow("count")]
    [DataRow("total")]
    public void Lexical_input_rejects_invalid_encoding_and_resource_limits(string mutation)
    {
        var question = mutation == "question" ? new string('a', 2049) : "cat";
        RagLexicalDocument[] documents = mutation switch {
            "text" => [new("a", new('a', 8001))], "unicode" => [new("a", "\ud800")],
            "duplicate" => [new("a", "cat"), new("a", "dog")],
            "count" => Enumerable.Range(0, 4097).Select(i => new RagLexicalDocument(i.ToString(), "cat")).ToArray(),
            "total" => Enumerable.Range(0, 263).Select(i => new RagLexicalDocument(i.ToString(), new('a', 8000))).ToArray(), _ => [new("a", "cat")] };
        Assert.ThrowsExactly<InvalidDataException>(() => RagLexicalRanker.Rank(question, documents));
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("unknown")]
    [DataRow("missing")]
    [DataRow("null")]
    [DataRow("nested-duplicate")]
    public void External_input_rejects_ambiguous_or_incomplete_json(string mutation)
    {
        var node = JsonSerializer.SerializeToNode(Input(), RagEvaluationJson.Options)!.AsObject();
        if (mutation == "unknown") node["unexpected"] = true;
        if (mutation == "missing") node.Remove("dimensions");
        if (mutation == "null") node["vectors"] = null;
        var json = node.ToJsonString();
        if (mutation == "duplicate") json = "{\"schemaVersion\":1," + json[1..];
        if (mutation == "nested-duplicate") json = json.Replace("\"chunkId\":", "\"chunkId\":\"injected\",\"chunkId\":", StringComparison.Ordinal);
        var path = RagEvaluationJson.TemporaryPath("retrieval-input"); File.WriteAllText(path, json);
        Assert.ThrowsExactly<JsonException>(() => RagRetrievalExecutionArtifacts.Read(path));
    }

    [TestMethod]
    public void Artifact_reader_bounds_bytes_and_accepts_bom_but_rejects_invalid_utf8()
    {
        var path = RagEvaluationJson.TemporaryPath("retrieval-input");
        File.WriteAllText(path, JsonSerializer.Serialize(Input(), RagEvaluationJson.Options), new UTF8Encoding(true));
        Assert.AreEqual(Input().DatasetSha256, RagRetrievalExecutionArtifacts.Read(path).DatasetSha256);
        File.WriteAllBytes(path, [0xff]); Assert.ThrowsExactly<DecoderFallbackException>(() => RagRetrievalExecutionArtifacts.Read(path));
        File.WriteAllBytes(path, []); Assert.ThrowsExactly<InvalidDataException>(() => RagRetrievalExecutionArtifacts.Read(path));
        using (var stream = File.Create(path)) stream.SetLength(8 * 1024 * 1024 + 1);
        Assert.ThrowsExactly<InvalidDataException>(() => RagRetrievalExecutionArtifacts.Read(path));
    }

    [TestMethod]
    public void Report_writer_never_overwrites_existing_evidence()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var report = RagRetrievalExecution.Execute(corpus, digest, Input());
        var path = RagEvaluationJson.TemporaryPath("retrieval-input");
        RagRetrievalExecutionArtifacts.Write(path, report); var original = File.ReadAllBytes(path);
        Assert.ThrowsExactly<IOException>(() => RagRetrievalExecutionArtifacts.Write(path, report)); CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Cancelled_report_write_does_not_create_an_output_file()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var report = RagRetrievalExecution.Execute(corpus, digest, Input());
        var path = RagEvaluationJson.TemporaryPath("retrieval-input");
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => RagRetrievalExecutionArtifacts.Write(path, report, stop.Token));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Machine_model_keys_reject_trailing_newlines(bool rerank)
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus(); var input = Input();
        input = rerank ? input with { RerankModelKey = "controlled-reranker-v1\n" }
            : input with { ModelKey = input.ModelKey + "\n",
                Vectors = input.Vectors.Select(x => x with { ModelKey = x.ModelKey + "\n" }).ToArray(),
                Queries = input.Queries.Select(x => x with { ModelKey = x.ModelKey + "\n" }).ToArray() };
        Assert.ThrowsExactly<InvalidDataException>(() => RagRetrievalExecution.Execute(corpus, digest, input));
    }

    [TestMethod]
    public void Execute_retrieval_evidence_and_write_report()
    {
        var path = Environment.GetEnvironmentVariable("FULLNET_AI_RETRIEVAL_EXECUTION_INPUT");
        var admitted = Environment.GetEnvironmentVariable("FULLNET_AI_RETRIEVAL_EXECUTION_CORPUS");
        var (corpus, digest) = string.IsNullOrWhiteSpace(admitted) ? RagEvaluationJson.LoadCorpus() : RagFeedbackAdmissionArtifacts.ReadCorpus(admitted);
        if (!string.IsNullOrWhiteSpace(admitted) && string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("admitted_retrieval_requires_external_vectors");
        var input = string.IsNullOrWhiteSpace(path) ? Input() : RagRetrievalExecutionArtifacts.Read(path);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var report = RagRetrievalExecution.Execute(corpus, digest, input, budget.Token);
        var output = Environment.GetEnvironmentVariable("FULLNET_AI_RETRIEVAL_EXECUTION_REPORT");
        if (string.IsNullOrWhiteSpace(output)) output = RagEvaluationJson.TemporaryPath("retrieval-execution");
        RagRetrievalExecutionArtifacts.Write(output, report, budget.Token); TestContext.AddResultFile(output);
    }
}
