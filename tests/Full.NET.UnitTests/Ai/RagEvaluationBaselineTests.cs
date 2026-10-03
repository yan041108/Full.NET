using System.Text.Json;
using Full.NET.UnitTests.Ai.Evaluation;

namespace Full.NET.UnitTests.Ai;

/// <summary>冻结数据、完整覆盖和独立安全门禁是后续真实知识库评估的前置条件。</summary>
[TestClass]
public sealed class RagEvaluationBaselineTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Frozen_corpus_covers_quality_and_security_categories()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus();
        Assert.AreEqual("3380bf8d0b8352df48093b1c0dee7d4216b0eeae30733cc37f04cd65e778ec6b", digest);
        foreach (var category in new[] { "terminology", "identifier", "table", "cross_paragraph", "version_conflict", "no_answer", "cross_tenant", "revoked", "prompt_injection", "deleted", "history_dependency" })
            Assert.IsTrue(corpus.Cases.Any(x => x.Category == category), category);
    }

    [TestMethod]
    public void Reference_is_only_an_offline_scoring_baseline()
    {
        var report = Score(Reference());
        Assert.IsTrue(report.Passed);
        Assert.AreEqual("not_measured", report.EndToEndStatus);
        Assert.AreEqual(1.0, report.RecallAtK);
        Assert.AreEqual(1.0, report.CitationValidity);
        Assert.AreEqual(1.0, report.RefusalAccuracy);
        Assert.AreEqual(0, report.SecurityFailureCases);
    }

    [TestMethod]
    public void One_security_failure_blocks_an_otherwise_perfect_run()
    {
        var run = Change("cross-tenant", x => x with { Answer = "SYNTHETIC-TENANT-B-704" });
        var report = Score(run);
        Assert.IsFalse(report.Passed);
        Assert.AreEqual(1, report.SafetyFailureCases);
        CollectionAssert.Contains(report.GateFailures, "security_failure");
    }

    [TestMethod]
    public void Security_failure_on_regular_case_also_blocks_gate()
    {
        var report = Score(Change("zh-terminology", x => x with { ContextChunkIds = ["slo-1", "tenant-b-secret"] }));
        Assert.IsFalse(report.Passed);
        Assert.AreEqual(1, report.SecurityFailureCases);
        Assert.AreEqual(0, report.SafetyFailureCases);
    }

    [TestMethod]
    public void Missing_citations_cannot_pass_with_vacuous_validity()
    {
        var run = Reference();
        var report = Score(run with { Results = run.Results.Select(x => x with { CitationChunkIds = [] }).ToArray() });
        Assert.IsNull(report.CitationValidity);
        Assert.AreEqual(0.0, report.AnswerRate);
        Assert.IsFalse(report.Passed);
    }

    [TestMethod]
    public void Unknown_semantic_review_blocks_candidate()
    {
        var report = Score(Change("table-limit", x => x with { SemanticReview = new("unknown", "", "") }));
        CollectionAssert.Contains(report.GateFailures, "semantic_support");
    }

    [TestMethod]
    public void Missing_result_cannot_improve_average_by_omission()
    {
        var run = Reference();
        Assert.ThrowsExactly<InvalidDataException>(() => Score(run with { Results = run.Results[..^1] }));
    }

    [TestMethod]
    public void Duplicate_result_cannot_replace_missing_case()
    {
        var run = Reference();
        Assert.ThrowsExactly<InvalidDataException>(() => Score(run with { Results = [.. run.Results[..^1], run.Results[0]] }));
    }

    [TestMethod]
    [DataRow("data")]
    [DataRow("digest")]
    [DataRow("policy")]
    public void Candidate_must_use_frozen_dataset_and_policy(string mismatch)
    {
        var run = Reference();
        run = mismatch switch
        {
            "data" => run with { DatasetVersion = "other" },
            "digest" => run with { DatasetSha256 = "other" },
            _ => run with { PolicyVersion = "other" }
        };
        Assert.ThrowsExactly<InvalidDataException>(() => Score(run));
    }

    [TestMethod]
    public void Unknown_usage_stays_unknown_in_report()
    {
        var report = Score(Change("table-limit", x => x with { Usage = new(null, null, null, null, "CNY") }));
        Assert.AreEqual(1, report.UnknownUsageCases);
        Assert.IsNull(report.ModelCalls);
        Assert.IsNull(report.InputTokens);
        Assert.IsNull(report.OutputTokens);
        Assert.IsNull(report.Cost);
    }

    [TestMethod]
    public void Different_currencies_are_not_summed()
    {
        var report = Score(Change("table-limit", x => x with { Usage = new(1, 10, 5, 0.1m, "USD") }));
        Assert.IsNull(report.Cost);
        CollectionAssert.AreEquivalent(new[] { "CNY", "USD" }, report.Currencies);
    }

    [TestMethod]
    public void Negative_usage_is_invalid_evidence()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Score(Change("table-limit", x => x with { Usage = new(1, -1, 0, 0m, "CNY") })));
    }

    [TestMethod]
    public void Unknown_run_kind_cannot_claim_end_to_end_execution()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Score(Reference() with { RunKind = "anything" }));
    }

    [TestMethod]
    public void End_to_end_run_cannot_relabel_offline_reference()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Score(Reference() with { RunKind = "end_to_end" }));
    }

    [TestMethod]
    public void End_to_end_evidence_is_reported_separately_from_fixture()
    {
        var run = Change("zh-terminology", x => x with { Usage = new(1, 20, 10, null, "CNY") });
        var report = Score(run with
        {
            RunKind = "end_to_end",
            Versions = new("controlled-model-v1", "prompt-v1", "parser-v1", "chunk-v1", "retrieval-v1", "index-v1")
        });
        Assert.AreEqual("reported", report.EndToEndStatus);
        Assert.IsNull(report.Cost);
    }

    [TestMethod]
    public void Null_chunk_in_corpus_is_invalid_evidence()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus();
        Assert.ThrowsExactly<InvalidDataException>(() => RagEvaluationScorer.ScoreRun(
            corpus with { Chunks = [null!] }, digest, Reference()));
    }

    [TestMethod]
    public void Unauthorized_expected_evidence_is_invalid_corpus()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus();
        var cases = corpus.Cases.Select(x => x.CaseId == "zh-terminology"
            ? x with { ExpectedChunkIds = ["tenant-b-secret"] } : x).ToArray();
        Assert.ThrowsExactly<InvalidDataException>(() => RagEvaluationScorer.ScoreRun(
            corpus with { Cases = cases }, digest, Reference()));
    }

    [TestMethod]
    public void Null_result_element_is_invalid_evidence()
    {
        var run = Reference();
        Assert.ThrowsExactly<InvalidDataException>(() => Score(run with { Results = [.. run.Results[..^1], null!] }));
    }

    [TestMethod]
    public void Empty_answer_does_not_count_as_answer_with_citation()
    {
        var report = Score(Change("table-limit", x => x with { Answer = " " }));
        Assert.IsFalse(report.Passed);
        CollectionAssert.Contains(report.GateFailures, "answer_rate");
    }

    [TestMethod]
    public void Non_finite_duration_is_invalid_evidence()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Score(Change("table-limit", x => x with { DurationMilliseconds = double.NaN })));
    }

    [TestMethod]
    public void Metrics_aggregate_known_usage_and_nearest_rank_latency()
    {
        var run = Reference();
        var report = Score(run with { Results = run.Results.Select((x, i) => x with
        {
            DurationMilliseconds = i + 1,
            Usage = new(1, 2, 3, 0.1m, "CNY")
        }).ToArray() });
        Assert.AreEqual(12, report.ModelCalls);
        Assert.AreEqual(24L, report.InputTokens);
        Assert.AreEqual(36L, report.OutputTokens);
        Assert.AreEqual(1.2m, report.Cost);
        Assert.AreEqual(6.5, report.MeanDurationMilliseconds);
        Assert.AreEqual(12.0, report.P95DurationMilliseconds);
    }

    [TestMethod]
    public void Strict_json_rejects_duplicate_unknown_missing_and_null_fields()
    {
        foreach (var json in new[] { "{\"verdict\":\"supported\",\"verdict\":\"unknown\",\"reviewer\":\"r\",\"evidence\":\"e\"}", "{\"verdict\":\"unknown\",\"reviewer\":\"\",\"evidence\":\"\",\"extra\":true}", "{\"verdict\":\"unknown\"}", "{\"verdict\":null,\"reviewer\":\"\",\"evidence\":\"\"}" })
            Assert.ThrowsExactly<JsonException>(() => RagEvaluationJson.Read<RagSemanticReview>(json));
    }

    [TestMethod]
    public void Dataset_digest_is_stable_across_checkout_line_endings()
    {
        Assert.AreEqual(RagEvaluationJson.Digest("a\nb\n"), RagEvaluationJson.Digest("a\r\nb\r\n"));
    }

    [TestMethod]
    public void Evaluate_frozen_results_and_write_reviewable_report()
    {
        var input = Environment.GetEnvironmentVariable("FULLNET_AI_EVALUATION_RESULTS");
        var run = string.IsNullOrWhiteSpace(input) ? Reference()
            : RagEvaluationJson.Read<RagEvaluationRun>(File.ReadAllText(input));
        var report = Score(run);
        var output = Environment.GetEnvironmentVariable("FULLNET_AI_EVALUATION_REPORT");
        if (string.IsNullOrWhiteSpace(output))
            output = Path.Combine(TestContext.TestResultsDirectory!, "rag-evaluation-report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, RagEvaluationJson.Options) + "\n");
        TestContext.AddResultFile(output);
        Assert.IsTrue(report.Passed, string.Join(", ", report.GateFailures));
    }

    private static RagEvaluationRun Reference() => RagEvaluationJson.LoadReference();

    private static RagEvaluationRun Change(string caseId, Func<RagEvaluationResult, RagEvaluationResult> change)
    {
        var run = Reference();
        return run with { Results = run.Results.Select(x => x.CaseId == caseId ? change(x) : x).ToArray() };
    }

    private static RagEvaluationReport Score(RagEvaluationRun run)
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus();
        return RagEvaluationScorer.ScoreRun(corpus, digest, run);
    }
}
