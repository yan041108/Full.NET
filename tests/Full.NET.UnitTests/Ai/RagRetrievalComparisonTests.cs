using System.Text.Json;
using Full.NET.UnitTests.Ai.Evaluation;

namespace Full.NET.UnitTests.Ai;

/// <summary>全部执行声明均为测试构造，验证决策规则，不代表真实模型或检索提升。</summary>
[TestClass]
public sealed class RagRetrievalComparisonTests
{
    [TestMethod]
    public void Offline_reference_is_scored_but_cannot_recommend_a_strategy_change()
    {
        var input = Input(false);
        var report = Compare(input);
        Assert.AreEqual("not_measured", report.EvidenceStatus);
        Assert.AreEqual("basic", report.RecommendedStrategyKey);
        Assert.IsTrue(report.Candidates.All(x => x.Score.Passed && !x.Eligible));
        Assert.IsTrue(report.Candidates.All(x => x.Blockers.Contains("execution_not_measured")));
    }

    [TestMethod]
    public void Measured_recall_gain_within_resource_limits_recommends_hybrid()
    {
        var report = Compare(Input());
        Assert.AreEqual("keyword_vector", report.RecommendedStrategyKey);
        Assert.AreEqual("eligible", report.PromotionStatus);
        var hybrid = report.Candidates.Single(x => x.StrategyKey == "keyword_vector");
        Assert.IsTrue(hybrid.Eligible);
        Assert.IsTrue(hybrid.RecallGain > 0.02);
        Assert.AreEqual(1m, hybrid.TokenRatio);
        Assert.AreEqual(1m, hybrid.CostRatio);
        Assert.AreEqual(1.0, hybrid.P95DurationRatio);
        Assert.AreEqual(1m, hybrid.CallRatio);
    }

    [TestMethod]
    public void Equal_quality_keeps_current_strategy_even_if_candidate_is_faster()
    {
        var input = Input();
        input = Change(input, "keyword_vector", run => run with { Results = input.Strategies[0].Run.Results });
        var report = Compare(input);
        Assert.AreEqual("basic", report.RecommendedStrategyKey);
        Assert.IsTrue(report.Candidates.All(x => x.Blockers.Contains("no_quality_gain")));
    }

    [TestMethod]
    [DataRow("input", "token_budget")]
    [DataRow("output", "token_budget")]
    [DataRow("cost", "cost_budget")]
    [DataRow("duration", "latency_budget")]
    [DataRow("calls", "call_budget")]
    public void Resource_regression_blocks_an_otherwise_better_candidate(string field, string blocker)
    {
        var input = ChangeResult(Input(), "keyword_vector", result => field switch
        {
            "input" => result with { Usage = result.Usage with { InputTokens = 2000 } },
            "output" => result with { Usage = result.Usage with { OutputTokens = 2000 } },
            "cost" => result with { Usage = result.Usage with { Cost = 1m } },
            "duration" => result with { DurationMilliseconds = 200 },
            _ => result with { Usage = result.Usage with { ModelCalls = 2 } }
        });
        var report = Compare(input);
        Assert.AreEqual("basic", report.RecommendedStrategyKey);
        CollectionAssert.Contains(report.Candidates[0].Blockers, blocker);
    }

    [TestMethod]
    [DataRow("input")]
    [DataRow("output")]
    [DataRow("cost")]
    [DataRow("calls")]
    public void Unknown_usage_in_one_case_cannot_be_treated_as_zero(string field)
    {
        var report = Compare(Change(Input(), "keyword_vector", run => run with
        {
            Results = run.Results.Select((x, i) => i != 0 ? x : x with { Usage = field switch
            {
                "input" => x.Usage with { InputTokens = null },
                "output" => x.Usage with { OutputTokens = null },
                "cost" => x.Usage with { Cost = null },
                _ => x.Usage with { ModelCalls = null }
            } }).ToArray()
        }));
        Assert.AreEqual("basic", report.RecommendedStrategyKey);
        CollectionAssert.Contains(report.Candidates[0].Blockers, "usage_unknown");
    }

    [TestMethod]
    public void Unknown_baseline_usage_blocks_all_promotions()
    {
        var report = Compare(ChangeResult(Input(), "basic", x => x with { Usage = x.Usage with { Cost = null } }));
        Assert.IsTrue(report.Candidates.All(x => x.Blockers.Contains("baseline_usage_unknown")));
        Assert.AreEqual("basic", report.RecommendedStrategyKey);
    }

    [TestMethod]
    public void Different_or_mixed_currency_is_never_compared_as_equal_cost()
    {
        foreach (var all in new[] { false, true })
        {
            var input = Change(Input(), "keyword_vector", run => run with
            {
                Results = run.Results.Select((x, i) => all || i == 0 ? x with { Usage = x.Usage with { Currency = "USD" } } : x).ToArray()
            });
            var report = Compare(input);
            CollectionAssert.Contains(report.Candidates[0].Blockers, "currency_mismatch");
            Assert.IsNull(report.Candidates[0].CostRatio);
        }
    }

    [TestMethod]
    public void Any_safety_failure_blocks_candidate_without_hiding_other_candidate_scores()
    {
        var report = Compare(Change(Input(), "keyword_vector", run => run with
        {
            Results = run.Results.Select(x => x.CaseId == "cross-tenant" ? x with { Answer = "SYNTHETIC-TENANT-B-704" } : x).ToArray()
        }));
        Assert.AreEqual(1, report.Candidates[0].Score.SecurityFailureCases);
        CollectionAssert.Contains(report.Candidates[0].Blockers, "quality_gate_failed");
        Assert.AreEqual(2, report.Candidates.Length);
        Assert.AreEqual("basic", report.RecommendedStrategyKey);
    }

    [TestMethod]
    public void Aggregate_recall_gain_cannot_hide_regression_on_a_different_case()
    {
        var report = Compare(Change(Input(), "keyword_vector", run => run with
        {
            Results = run.Results.Select(x => x.CaseId == "zh-terminology" ? x with
            { CandidateChunkIds = [], ContextChunkIds = [], CitationChunkIds = [] } : x).ToArray()
        }));
        CollectionAssert.Contains(report.Candidates[0].Blockers, "case_regression");
        Assert.AreEqual("basic", report.RecommendedStrategyKey);
    }

    [TestMethod]
    public void Failed_baseline_and_zero_timing_do_not_produce_a_promotion()
    {
        var failed = Compare(ChangeResult(Input(), "basic", x => x with { SemanticReview = new("unknown", "", "") }));
        Assert.IsTrue(failed.Candidates.All(x => x.Blockers.Contains("baseline_quality_gate_failed")));
        var untimed = Compare(ChangeResult(Input(), "keyword_vector", x => x with { DurationMilliseconds = 0 }));
        CollectionAssert.Contains(untimed.Candidates[0].Blockers, "timing_not_measured");
    }

    [TestMethod]
    public void Zero_baseline_cost_allows_only_zero_cost_and_has_no_infinite_ratio()
    {
        var input = ChangeResult(Input(), "basic", x => x with { Usage = x.Usage with { Cost = 0 } });
        var report = Compare(input);
        CollectionAssert.Contains(report.Candidates[0].Blockers, "cost_budget");
        Assert.IsNull(report.Candidates[0].CostRatio);
        report = Compare(ChangeResult(input, "keyword_vector", x => x with { Usage = x.Usage with { Cost = 0 } }));
        Assert.IsTrue(report.Candidates[0].Eligible);
        Assert.AreEqual(1m, report.Candidates[0].CostRatio);
    }

    [TestMethod]
    public void Tied_candidates_prefer_hybrid_regardless_of_input_order()
    {
        var input = Input();
        var hybrid = input.Strategies[1].Run;
        input = Change(input, "rerank", run => run with { Results = hybrid.Results });
        var report = Compare(input with { Strategies = input.Strategies.Reverse().ToArray() });
        Assert.AreEqual("keyword_vector", report.RecommendedStrategyKey);
        CollectionAssert.AreEqual(new[] { "keyword_vector", "rerank" }, report.Candidates.Select(x => x.StrategyKey).ToArray());
    }

    [TestMethod]
    public void Lower_resource_candidate_wins_when_quality_is_tied()
    {
        var input = Input();
        var hybrid = input.Strategies[1].Run;
        input = Change(input, "rerank", run => run with { Results = hybrid.Results.Select(x => x with { DurationMilliseconds = 50 }).ToArray() });
        Assert.AreEqual("rerank", Compare(input).RecommendedStrategyKey);
    }

    [TestMethod]
    [DataRow("model")]
    [DataRow("prompt")]
    [DataRow("parser")]
    [DataRow("chunking")]
    [DataRow("index")]
    [DataRow("dataset")]
    [DataRow("digest")]
    [DataRow("policy")]
    [DataRow("kind")]
    public void Different_frozen_inputs_are_rejected_instead_of_mislabeled_as_strategy_gain(string field)
    {
        var input = Change(Input(), "keyword_vector", run => field switch
        {
            "model" => run with { Versions = run.Versions with { Model = "other" } },
            "prompt" => run with { Versions = run.Versions with { Prompt = "other" } },
            "parser" => run with { Versions = run.Versions with { Parser = "other" } },
            "chunking" => run with { Versions = run.Versions with { Chunking = "other" } },
            "index" => run with { Versions = run.Versions with { Index = "other" } },
            "dataset" => run with { DatasetVersion = "other" },
            "digest" => run with { DatasetSha256 = new string('a', 64) },
            "policy" => run with { PolicyVersion = "other" },
            _ => run with { RunKind = "offline_reference" }
        });
        Assert.ThrowsExactly<InvalidDataException>(() => Compare(input));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("unknown")]
    [DataRow("schema")]
    [DataRow("null")]
    [DataRow("reused_version")]
    public void Incomplete_ambiguous_or_unknown_strategy_sets_are_invalid(string field)
    {
        var input = Input();
        input = field switch
        {
            "missing" => input with { Strategies = input.Strategies[..2] },
            "duplicate" => input with { Strategies = [input.Strategies[0], input.Strategies[1], input.Strategies[1]] },
            "unknown" => input with { Strategies = [input.Strategies[0], input.Strategies[1], input.Strategies[2] with { StrategyKey = "custom" }] },
            "schema" => input with { SchemaVersion = 2 },
            "null" => input with { Strategies = [input.Strategies[0], input.Strategies[1], null!] },
            _ => Change(input, "rerank", run => run with { Versions = run.Versions with { Retrieval = input.Strategies[1].Run.Versions.Retrieval } })
        };
        Assert.ThrowsExactly<InvalidDataException>(() => Compare(input));
    }

    [TestMethod]
    public void Evidence_digest_changes_when_an_answer_changes_but_report_does_not_copy_it()
    {
        var input = Input();
        var before = Compare(input);
        var after = Compare(ChangeResult(input, "keyword_vector", x => x with { Answer = "private-test-sentinel" }));
        Assert.AreNotEqual(before.Candidates[0].EvidenceSha256, after.Candidates[0].EvidenceSha256);
        Assert.IsFalse(JsonSerializer.Serialize(after, RagEvaluationJson.Options).Contains("private-test-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("huge_candidate")]
    [DataRow("tiny_baseline")]
    [DataRow("huge_baseline")]
    public void Extreme_decimal_costs_preserve_exact_budget_and_reviewable_fallback(string scenario)
    {
        var baseline = scenario switch { "tiny_baseline" => 0.0000000000000000000000000005m, "huge_baseline" => decimal.MaxValue, _ => 0.01m };
        var candidate = scenario == "tiny_baseline" ? 0.0000000000000000000000000006m : decimal.MaxValue;
        RagRetrievalComparisonInput Cost(RagRetrievalComparisonInput input, string key, decimal amount)
            => Change(input, key, run => run with { Results = run.Results.Select((x, i) => x with { Usage = x.Usage with { Cost = i == 0 ? amount : 0m } }).ToArray() });
        var report = Compare(Cost(Cost(Input(), "basic", baseline), "keyword_vector", candidate));
        Assert.AreEqual(scenario == "huge_baseline" ? "keyword_vector" : "basic", report.RecommendedStrategyKey);
        if (scenario != "huge_baseline") CollectionAssert.Contains(report.Candidates[0].Blockers, "cost_budget");
        Assert.IsFalse(string.IsNullOrWhiteSpace(JsonSerializer.Serialize(report, RagEvaluationJson.Options)));
    }

    [TestMethod]
    public void Rounded_display_ratio_cannot_hide_a_cost_above_the_exact_limit()
    {
        var input = Input();
        foreach (var key in new[] { "basic", "keyword_vector" })
            input = Change(input, key, run => run with { Results = run.Results.Select((x, i) => x with
            { Usage = x.Usage with { Cost = i != 0 ? 0m : key == "basic" ? 3m : 3.3000000000000000000000000001m } }).ToArray() });
        var report = Compare(input);
        Assert.AreEqual(1.10m, report.Candidates[0].CostRatio);
        CollectionAssert.Contains(report.Candidates[0].Blockers, "cost_budget");
        Assert.AreEqual("basic", report.RecommendedStrategyKey);
    }

    [TestMethod]
    public void Subnormal_duration_rounding_cannot_hide_a_latency_regression()
    {
        var input = ChangeResult(Input(), "basic", x => x with { DurationMilliseconds = 3 * double.Epsilon });
        input = ChangeResult(input, "keyword_vector", x => x with { DurationMilliseconds = 4 * double.Epsilon });
        var report = Compare(input);
        CollectionAssert.Contains(report.Candidates[0].Blockers, "latency_budget");
        Assert.AreEqual("basic", report.RecommendedStrategyKey);
    }

    [TestMethod]
    public void Unrepresentable_latency_ratio_still_writes_a_finite_rejection_report()
    {
        var report = Compare(ChangeResult(Input(), "basic", x => x with { DurationMilliseconds = double.Epsilon }));
        CollectionAssert.Contains(report.Candidates[0].Blockers, "latency_budget");
        Assert.IsNull(report.Candidates[0].P95DurationRatio);
        Assert.IsFalse(string.IsNullOrWhiteSpace(JsonSerializer.Serialize(report, RagEvaluationJson.Options)));
    }

    [TestMethod]
    public void Finite_large_case_durations_do_not_overflow_the_report_mean()
    {
        var input = Input();
        foreach (var key in new[] { "basic", "keyword_vector", "rerank" })
            input = ChangeResult(input, key, x => x with { DurationMilliseconds = double.MaxValue });
        var report = Compare(input);
        Assert.IsTrue(double.IsFinite(report.Baseline.MeanDurationMilliseconds));
        Assert.IsFalse(string.IsNullOrWhiteSpace(JsonSerializer.Serialize(report, RagEvaluationJson.Options)));
    }

    [TestMethod]
    [DataRow("input")]
    [DataRow("output")]
    [DataRow("calls")]
    [DataRow("cost")]
    public void Aggregate_usage_overflow_is_invalid_evidence_with_a_stable_failure_type(string field)
    {
        var input = ChangeResult(Input(), "keyword_vector", x => x with { Usage = field switch
        {
            "input" => x.Usage with { InputTokens = long.MaxValue },
            "output" => x.Usage with { OutputTokens = long.MaxValue },
            "calls" => x.Usage with { ModelCalls = int.MaxValue },
            _ => x.Usage with { Cost = decimal.MaxValue }
        } });
        Assert.ThrowsExactly<InvalidDataException>(() => Compare(input));
    }

    internal static RagRetrievalComparisonInput Input(bool measured = true)
    {
        var reference = RagEvaluationJson.LoadReference();
        // 有意降低基础检索的一项召回；答案/评审均为构造证据，不能用于生产启用。
        RagEvaluationRun Run(string key, bool improve) => reference with
        {
            RunKind = measured ? "end_to_end" : "offline_reference",
            Versions = new(measured ? "test-provider/model-v1" : "none-offline-reference", "test-prompt-v1", "synthetic-v1", "synthetic-v1", key + "-v1", "test-index-v1"),
            Results = reference.Results.Select(x =>
            {
                var result = x with { Usage = measured ? new(1, 100, 10, 0.01m, "CNY") : x.Usage, DurationMilliseconds = measured ? 100 : 0 };
                return !improve && x.CaseId == "cross-paragraph" ? result with
                { CandidateChunkIds = ["leave-1"], ContextChunkIds = ["leave-1"], CitationChunkIds = ["leave-1"] } : result;
            }).ToArray()
        };
        return new(1, [new("basic", Run("basic", false)), new("keyword_vector", Run("keyword_vector", true)), new("rerank", Run("rerank", false))]);
    }

    private static RagRetrievalComparisonInput Change(RagRetrievalComparisonInput input, string key, Func<RagEvaluationRun, RagEvaluationRun> change)
        => input with { Strategies = input.Strategies.Select(x => x.StrategyKey == key ? x with { Run = change(x.Run) } : x).ToArray() };

    private static RagRetrievalComparisonInput ChangeResult(RagRetrievalComparisonInput input, string key, Func<RagEvaluationResult, RagEvaluationResult> change)
        => Change(input, key, run => run with { Results = run.Results.Select(change).ToArray() });

    private static RagRetrievalComparisonReport Compare(RagRetrievalComparisonInput input)
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus();
        return RagRetrievalComparison.Compare(corpus, digest, input);
    }
}
