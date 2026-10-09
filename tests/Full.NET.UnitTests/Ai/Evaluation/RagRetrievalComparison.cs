using System.Numerics;
using System.Text.Json;

namespace Full.NET.UnitTests.Ai.Evaluation;

/// <summary>比较基础检索、关键词向量融合和重排；任何缺失证据都保留基础策略。</summary>
internal static class RagRetrievalComparison
{
    private static readonly string[] Keys = ["basic", "keyword_vector", "rerank"];
    private static readonly RagRetrievalComparisonPolicy Policy = new("rag-strategy-comparison-v1", 0.02, 1.10m, 1.10m, 1.20m, 1.25m);

    internal static RagRetrievalComparisonReport Compare(RagEvaluationCorpus corpus, string digest,
        RagRetrievalComparisonInput input)
    {
        if (input is null || input.SchemaVersion != 1 || input.Strategies is null || input.Strategies.Length != Keys.Length
            || input.Strategies.Any(x => x is null || x.Run is null)
            || !input.Strategies.Select(x => x.StrategyKey).ToHashSet(StringComparer.Ordinal).SetEquals(Keys))
            throw new InvalidDataException("必须提交唯一且完整的三策略证据。");
        var strategies = Keys.Select(key => input.Strategies.Single(x => x.StrategyKey == key)).ToArray();
        // 每份证据重新评分，不接受候选自报 Passed、汇总指标或质量门槛。
        var scores = strategies.Select(x => RagEvaluationScorer.ScoreRun(corpus, digest, x.Run)).ToArray();
        var baselineRun = strategies[0].Run;
        if (strategies.Select(x => x.Run.Versions.Retrieval).Distinct(StringComparer.Ordinal).Count() != Keys.Length
            || strategies.Any(x => x.Run.RunKind != baselineRun.RunKind || !SameInputs(x.Run.Versions, baselineRun.Versions)))
            throw new InvalidDataException("仅检索版本允许不同；模型、Prompt、解析、分块、索引和运行类型必须一致。");
        var baseline = scores[0];
        var decisions = strategies.Skip(1).Select((strategy, index) => Decide(strategy, scores[index + 1], baseline, baselineRun)).ToArray();
        // 相同收益先比较费用、Token、P95、调用，完全相同则按封闭策略顺序稳定选择。
        var winner = decisions.Where(x => x.Eligible).OrderByDescending(x => x.RecallGain + x.AnswerRateGain)
            .ThenBy(x => x.CostRatio).ThenBy(x => x.TokenRatio).ThenBy(x => x.P95DurationRatio).ThenBy(x => x.CallRatio).FirstOrDefault();
        return new(1, baseline.EndToEndStatus, "basic", winner?.StrategyKey ?? "basic",
            winner is null ? "retain_current" : "eligible", Policy, Digest(baselineRun), baseline, decisions);
    }

    private static RagRetrievalStrategyDecision Decide(RagRetrievalStrategyRun strategy, RagEvaluationReport score,
        RagEvaluationReport baseline, RagEvaluationRun baselineRun)
    {
        var blockers = new List<string>();
        var recallGain = (score.RecallAtK ?? 0) - (baseline.RecallAtK ?? 0);
        var answerGain = (score.AnswerRate ?? 0) - (baseline.AnswerRate ?? 0);
        if (score.EndToEndStatus != "reported") blockers.Add("execution_not_measured");
        if (!baseline.Passed) blockers.Add("baseline_quality_gate_failed");
        if (!score.Passed) blockers.Add("quality_gate_failed");
        if (score.Cases.Any(current => Regressed(current, baseline.Cases.Single(x => x.CaseId == current.CaseId))))
            blockers.Add("case_regression");
        if (Math.Max(recallGain, answerGain) < Policy.MinimumQualityGain) blockers.Add("no_quality_gain");
        if (baseline.UnknownUsageCases > 0) blockers.Add("baseline_usage_unknown");
        if (score.UnknownUsageCases > 0) blockers.Add("usage_unknown");
        var sameCurrency = baseline.Currencies.Length == 1 && score.Currencies.Length == 1
            && baseline.Currencies[0] == score.Currencies[0];
        if (!sameCurrency) blockers.Add("currency_mismatch");
        var tokens = TotalTokens(score); var baselineTokens = TotalTokens(baseline);
        var tokenRatio = Ratio(tokens, baselineTokens);
        var costRatio = sameCurrency ? Ratio(score.Cost, baseline.Cost) : null;
        var callRatio = Ratio(score.ModelCalls, baseline.ModelCalls);
        if (tokens.HasValue && baselineTokens.HasValue && Exceeds(tokens.Value, baselineTokens.Value, Policy.MaximumTokenRatio)) blockers.Add("token_budget");
        if (sameCurrency && score.Cost.HasValue && baseline.Cost.HasValue && Exceeds(score.Cost.Value, baseline.Cost.Value, Policy.MaximumCostRatio)) blockers.Add("cost_budget");
        if (score.ModelCalls.HasValue && baseline.ModelCalls.HasValue && Exceeds(score.ModelCalls.Value, baseline.ModelCalls.Value, Policy.MaximumCallRatio)) blockers.Add("call_budget");
        var rawDurationRatio = baseline.P95DurationMilliseconds > 0 ? score.P95DurationMilliseconds / baseline.P95DurationMilliseconds : double.NaN;
        double? durationRatio = double.IsFinite(rawDurationRatio) ? rawDurationRatio : null;
        if (strategy.Run.Results.Any(x => x.DurationMilliseconds <= 0) || baselineRun.Results.Any(x => x.DurationMilliseconds <= 0))
            blockers.Add("timing_not_measured");
        if (ExceedsDuration(score.P95DurationMilliseconds, baseline.P95DurationMilliseconds, Policy.MaximumDurationRatio)) blockers.Add("latency_budget");
        return new(strategy.StrategyKey, Digest(strategy.Run), score, recallGain, answerGain,
            tokenRatio, costRatio, durationRatio, callRatio, blockers.Count == 0, blockers.ToArray());
    }

    private static bool SameInputs(RagEvaluationVersions current, RagEvaluationVersions baseline)
        => current.Model == baseline.Model && current.Prompt == baseline.Prompt && current.Parser == baseline.Parser
            && current.Chunking == baseline.Chunking && current.Index == baseline.Index;

    private static bool Regressed(RagEvaluationCaseScore current, RagEvaluationCaseScore baseline)
        => baseline.RecallAtK.HasValue && !(current.RecallAtK >= baseline.RecallAtK)
            || baseline.AnsweredWithCitation && !current.AnsweredWithCitation
            || baseline.RefusalCorrect && !current.RefusalCorrect
            || baseline.SemanticSupported && !current.SemanticSupported;

    // 使用 decimal 合并两类 Token，避免先在 long 中加总造成环绕或比例失真。
    private static decimal? TotalTokens(RagEvaluationReport score)
        => score.InputTokens.HasValue && score.OutputTokens.HasValue ? (decimal)score.InputTokens.Value + score.OutputTokens.Value : null;

    // 零费用可代表本地免费模型；零分母下新增费用阻断，报告不输出 Infinity。
    private static decimal? Ratio(decimal? value, decimal? baseline)
    {
        if (!value.HasValue || !baseline.HasValue) return null;
        if (baseline == 0) return value == 0 ? 1m : null;
        try { return value / baseline; }
        catch (OverflowException) { return null; }
    }

    // 比例只用于展示；将 decimal 的整数尾数与十进制位数交叉相乘，避免溢出和门槛舍入。
    private static bool Exceeds(decimal value, decimal baseline, decimal limit)
    {
        var (amount, scale) = DecimalParts(value); var (basis, basisScale) = DecimalParts(baseline);
        var (maximum, maximumScale) = DecimalParts(limit);
        return amount * BigInteger.Pow(10, basisScale + maximumScale) > basis * maximum * BigInteger.Pow(10, scale);
    }

    private static (BigInteger Value, int Scale) DecimalParts(decimal value)
    {
        var bits = decimal.GetBits(value);
        var magnitude = new BigInteger((uint)bits[0]) + (new BigInteger((uint)bits[1]) << 32) + (new BigInteger((uint)bits[2]) << 64);
        return ((bits[3] & int.MinValue) == 0 ? magnitude : -magnitude, (bits[3] >> 16) & 0xff);
    }

    // IEEE 754 次正规数同样按实际二进制值比较，不能将 3ε × 1.2 舍入成 4ε 后放行。
    private static bool ExceedsDuration(double value, double baseline, decimal limit)
    {
        var (amount, exponent) = DoubleParts(value); var (basis, basisExponent) = DoubleParts(baseline);
        var (maximum, maximumScale) = DecimalParts(limit);
        var commonExponent = Math.Min(exponent, basisExponent);
        return (amount * BigInteger.Pow(10, maximumScale) << (exponent - commonExponent))
            > (basis * maximum << (basisExponent - commonExponent));
    }

    private static (BigInteger Value, int Exponent) DoubleParts(double value)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7ff);
        var fraction = bits & ((1UL << 52) - 1);
        return exponent == 0 ? (new BigInteger(fraction), -1074) : (new BigInteger(fraction | (1UL << 52)), exponent - 1075);
    }

    private static string Digest(RagEvaluationRun run) => RagEvaluationJson.Digest(JsonSerializer.Serialize(run, RagEvaluationJson.Options));
}
