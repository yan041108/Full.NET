using System.Text.Json;
using Full.NET.UnitTests.Ai.Evaluation;

namespace Full.NET.UnitTests.Ai;

/// <summary>报告入口必须拒绝模糊输入和覆盖；临时文件仅由本测试创建和删除。</summary>
[TestClass]
public sealed class RagRetrievalComparisonArtifactTests
{
    private string _directory = null!;
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize() => _directory = Directory.CreateTempSubdirectory("fullnet-rag-comparison-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public void Evidence_round_trip_preserves_full_observations()
    {
        var input = RagRetrievalComparisonTests.Input();
        var path = Path.Combine(_directory, "observations.json");
        File.WriteAllText(path, JsonSerializer.Serialize(input, RagEvaluationJson.Options));
        var read = RagRetrievalComparisonArtifacts.Read(path);
        Assert.AreEqual(input.Strategies[1].Run.Results[0].Answer, read.Strategies[1].Run.Results[0].Answer);
        Assert.AreEqual(3, read.Strategies.Length);
    }

    [TestMethod]
    [DataRow("{\"schemaVersion\":1,\"schemaVersion\":1,\"strategies\":[]}")]
    [DataRow("{\"schemaVersion\":1,\"strategies\":[],\"passed\":true}")]
    [DataRow("{\"schemaVersion\":1}")]
    public void Duplicate_unknown_and_missing_json_fields_are_rejected(string json)
    {
        var path = Path.Combine(_directory, "invalid.json"); File.WriteAllText(path, json);
        Assert.ThrowsExactly<JsonException>(() => RagRetrievalComparisonArtifacts.Read(path));
    }

    [TestMethod]
    public void Oversize_evidence_is_rejected_before_json_parse()
    {
        var path = Path.Combine(_directory, "large.json");
        File.WriteAllBytes(path, new byte[8 * 1024 * 1024 + 1]);
        Assert.ThrowsExactly<InvalidDataException>(() => RagRetrievalComparisonArtifacts.Read(path));
    }

    [TestMethod]
    public void New_report_is_valid_json_and_existing_report_is_never_overwritten()
    {
        var (corpus, digest) = RagEvaluationJson.LoadCorpus();
        var report = RagRetrievalComparison.Compare(corpus, digest, RagRetrievalComparisonTests.Input(false));
        var path = Path.Combine(_directory, "nested", "report.json");
        RagRetrievalComparisonArtifacts.Write(path, report);
        var original = File.ReadAllText(path);
        Assert.AreEqual("basic", RagEvaluationJson.Read<RagRetrievalComparisonReport>(original).RecommendedStrategyKey);
        Assert.ThrowsExactly<IOException>(() => RagRetrievalComparisonArtifacts.Write(path, report));
        Assert.AreEqual(original, File.ReadAllText(path));
    }

    [TestMethod]
    public void Evaluate_strategy_evidence_and_write_reviewable_report()
    {
        var inputPath = Environment.GetEnvironmentVariable("FULLNET_AI_STRATEGY_COMPARISON_INPUT");
        var external = !string.IsNullOrWhiteSpace(inputPath);
        var input = external ? RagRetrievalComparisonArtifacts.Read(inputPath!) : ReferenceInput();
        var (corpus, digest) = RagEvaluationJson.LoadCorpus();
        var report = RagRetrievalComparison.Compare(corpus, digest, input);
        var output = Environment.GetEnvironmentVariable("FULLNET_AI_STRATEGY_COMPARISON_REPORT");
        if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(TestContext.TestResultsDirectory!, "rag-strategy-comparison-" + Guid.NewGuid().ToString("N") + ".json");
        RagRetrievalComparisonArtifacts.Write(output, report);
        TestContext.AddResultFile(output);
        // 外部候选无合格提升时先保留失败报告，再使门禁退出非零；默认参考只验证工具链。
        Assert.IsTrue(external ? report.PromotionStatus == "eligible" : report.Baseline.Passed && report.Candidates.All(x => x.Score.Passed),
            report.PromotionStatus + ": " + string.Join(",", report.Candidates.SelectMany(x => x.Blockers).Distinct()));
    }

    private static RagRetrievalComparisonInput ReferenceInput()
    {
        var reference = RagEvaluationJson.LoadReference();
        return new(1, new[] { "basic", "keyword_vector", "rerank" }.Select(key => new RagRetrievalStrategyRun(key,
            reference with { Versions = reference.Versions with { Retrieval = "reference-" + key + "-v1" } })).ToArray());
    }
}
