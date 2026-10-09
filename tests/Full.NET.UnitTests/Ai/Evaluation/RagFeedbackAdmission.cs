using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

namespace Full.NET.UnitTests.Ai.Evaluation;

/// <summary>审核标记是受控操作人的声明，不认证身份，也不授予生产问答或来源的读取权限。</summary>
internal sealed record RagFeedbackReview(string ReviewerKey, string ContentSha256, string DecisionKey,
    bool SourceUseAllowed, bool RedactionConfirmed, bool InstructionRiskReviewed, string EvidenceKey);
internal sealed record RagFeedbackSample(string SampleKey, string AuthorKey, string SourceSha256,
    RagEvaluationCase Case, RagEvaluationChunk[] Chunks, RagFeedbackReview RedactionReview, RagFeedbackReview AdmissionReview);
internal sealed record RagFeedbackAdmissionInput(int SchemaVersion, string PolicyVersion, string BaseDatasetSha256,
    string DatasetVersion, RagFeedbackSample[] Samples);
internal sealed record RagFeedbackAdmissionReceipt(string CaseId, string ContentSha256, string PayloadSha256);
internal sealed record RagFeedbackAdmissionArtifact(int SchemaVersion, string PolicyVersion, string ReviewVerificationStatus,
    string BaseDatasetSha256, string CorpusSha256, RagEvaluationCorpus Corpus, RagFeedbackAdmissionReceipt[] Receipts);

/// <summary>人工脱敏样本只能追加冻结语料；保留全部旧案例、来源与门槛，工具不执行任何反馈指令。</summary>
internal static partial class RagFeedbackAdmission
{
    internal const string PolicyVersion = "rag-feedback-admission-v1";
    internal const string BaseDigest = "3380bf8d0b8352df48093b1c0dee7d4216b0eeae30733cc37f04cd65e778ec6b";
    private const int MaximumCharacters = 131072;

    internal static string ContentDigest(RagFeedbackSample sample)
        => RagEvaluationJson.Digest(JsonSerializer.Serialize(new { sample.SampleKey, sample.AuthorKey, sample.SourceSha256, sample.Case, sample.Chunks }, RagEvaluationJson.Options));

    private static string PayloadDigest(RagEvaluationCase testCase, RagEvaluationChunk[] chunks)
        => RagEvaluationJson.Digest(JsonSerializer.Serialize(new { Case = testCase, Chunks = chunks }, RagEvaluationJson.Options));

    internal static RagFeedbackAdmissionArtifact Admit(RagFeedbackAdmissionInput input)
    {
        Require(input is not null && input.SchemaVersion == 1 && input.PolicyVersion == PolicyVersion
            && input.BaseDatasetSha256 == BaseDigest && DatasetKey().IsMatch(input.DatasetVersion)
            && input.Samples is { Length: > 0 and <= 32 }, "invalid_feedback_admission_package");
        var (baseline, digest) = RagEvaluationJson.LoadCorpus();
        Require(digest == BaseDigest, "feedback_admission_baseline_changed");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var chunks = new List<RagEvaluationChunk>(); var cases = new List<RagEvaluationCase>();
        var chunkIds = new HashSet<string>(StringComparer.Ordinal);
        var receipts = new List<RagFeedbackAdmissionReceipt>(); var characters = 0;
        foreach (var sample in input.Samples)
        {
            ValidateSample(sample);
            Require(keys.Add(sample.SampleKey), "duplicate_feedback_sample");
            Require(sample.Chunks.All(x => chunkIds.Add(x.ChunkId)), "duplicate_feedback_chunk");
            var contentDigest = ContentDigest(sample);
            var redaction = sample.RedactionReview; var admission = sample.AdmissionReview;
            // 不同标签只能阻止明显的自审或同人双审；实际身份、来源许可与脱敏证据仍须在受控环境核对。
            Require(ValidReview(redaction, contentDigest) && ValidReview(admission, contentDigest)
                && redaction.ReviewerKey != admission.ReviewerKey && redaction.ReviewerKey != sample.AuthorKey
                && admission.ReviewerKey != sample.AuthorKey, "feedback_sample_review_required");
            characters = checked(characters + sample.Case.Question.Length + sample.Chunks.Sum(x => x.Text.Length));
            Require(characters <= MaximumCharacters, "feedback_admission_character_budget");
            cases.Add(sample.Case); chunks.AddRange(sample.Chunks);
            receipts.Add(new(sample.Case.CaseId, contentDigest, PayloadDigest(sample.Case, sample.Chunks)));
        }
        var corpus = baseline with { DatasetVersion = input.DatasetVersion, Cases = [.. baseline.Cases, .. cases], Chunks = [.. baseline.Chunks, .. chunks] };
        return new(1, PolicyVersion, "operator_attested", BaseDigest, CorpusDigest(corpus), corpus, receipts.ToArray());
    }

    internal static (RagEvaluationCorpus Corpus, string Digest) Verify(RagFeedbackAdmissionArtifact artifact)
    {
        Require(artifact is not null && artifact.SchemaVersion == 1 && artifact.PolicyVersion == PolicyVersion
            && artifact.ReviewVerificationStatus == "operator_attested" && artifact.BaseDatasetSha256 == BaseDigest
            && artifact.Corpus is not null && artifact.Receipts is { Length: > 0 and <= 32 }, "invalid_feedback_admission_artifact");
        var (baseline, digest) = RagEvaluationJson.LoadCorpus();
        var corpus = artifact.Corpus;
        Require(digest == BaseDigest && DatasetKey().IsMatch(corpus.DatasetVersion) && corpus.Policy == baseline.Policy
            && corpus.Cases is not null && corpus.Chunks is not null && artifact.CorpusSha256 == CorpusDigest(corpus), "feedback_admission_integrity_failed");
        // 基线前缀必须逐字保留，防止替换安全案例、来源或门槛后仍冒充同一评估集。
        Require(corpus.Cases.Length == baseline.Cases.Length + artifact.Receipts.Length
            && corpus.Chunks.Length > baseline.Chunks.Length && corpus.Chunks.Length <= baseline.Chunks.Length + artifact.Receipts.Length * 16
            && Same(corpus.Cases.Take(baseline.Cases.Length), baseline.Cases)
            && Same(corpus.Chunks.Take(baseline.Chunks.Length), baseline.Chunks), "feedback_admission_baseline_changed");
        var keys = new HashSet<string>(StringComparer.Ordinal); var characters = 0;
        Require(corpus.Chunks.Select(x => x?.ChunkId).Distinct(StringComparer.Ordinal).Count() == corpus.Chunks.Length, "duplicate_feedback_chunk");
        foreach (var (testCase, index) in corpus.Cases.Skip(baseline.Cases.Length).Select((x, i) => (x, i)))
        {
            Require(testCase is not null && testCase.CaseId.StartsWith("reviewed-", StringComparison.Ordinal), "invalid_feedback_sample");
            var key = testCase.CaseId[9..];
            var sampleChunks = corpus.Chunks.Skip(baseline.Chunks.Length).Where(x => x is not null && x.SourceVersion == "reviewed-" + key + "-v1").ToArray();
            var sample = new RagFeedbackSample(key, "operator", new('a', 64), testCase, sampleChunks, null!, null!);
            ValidateSample(sample);
            Require(keys.Add(key) && artifact.Receipts[index] is { } receipt && receipt.CaseId == testCase.CaseId
                && HashKey().IsMatch(receipt.ContentSha256) && receipt.PayloadSha256 == PayloadDigest(testCase, sampleChunks), "invalid_feedback_admission_receipt");
            characters = checked(characters + testCase.Question.Length + sampleChunks.Sum(x => x.Text.Length));
        }
        Require(characters <= MaximumCharacters && corpus.Chunks.Skip(baseline.Chunks.Length).All(x => x is not null
            && keys.Any(key => x.SourceVersion == "reviewed-" + key + "-v1")), "invalid_feedback_admission_artifact");
        return (corpus, artifact.CorpusSha256);
    }

    internal static RagEvaluationReport Score(RagFeedbackAdmissionArtifact artifact, RagEvaluationRun run)
    {
        var (corpus, digest) = Verify(artifact);
        // 外部文件路径不是实际执行证明；至少拒绝参考类型、空版本和未测时延，声明真实性仍须核对原始运行记录。
        Require(run is not null && run.RunKind == "end_to_end" && run.Versions is not null && run.Results is not null
            && run.Results.Length == corpus.Cases.Length && run.Results.Select(x => x?.CaseId).Distinct(StringComparer.Ordinal).Count() == run.Results.Length
            && run.Results.All(x => x is not null && x.Usage is not null && double.IsFinite(x.DurationMilliseconds) && x.DurationMilliseconds > 0), "feedback_evaluation_execution_required");
        Require(new[] { run.Versions.Model, run.Versions.Prompt, run.Versions.Parser, run.Versions.Chunking, run.Versions.Retrieval, run.Versions.Index }
            .All(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("none-", StringComparison.Ordinal) && !x.StartsWith("reference-", StringComparison.Ordinal)), "feedback_evaluation_execution_required");
        foreach (var testCase in corpus.Cases.Where(x => !x.ShouldRefuse))
            Require(run.Results.SingleOrDefault(x => x.CaseId == testCase.CaseId)?.Usage?.ModelCalls is null or > 0
                && run.Results.Any(x => x.CaseId == testCase.CaseId), "feedback_evaluation_execution_required");
        return RagEvaluationScorer.ScoreRun(corpus, digest, run);
    }

    private static bool Same<T>(IEnumerable<T> left, IEnumerable<T> right)
        => JsonSerializer.Serialize(left, RagEvaluationJson.Options) == JsonSerializer.Serialize(right, RagEvaluationJson.Options);
    private static string CorpusDigest(RagEvaluationCorpus corpus) => RagEvaluationJson.Digest(JsonSerializer.Serialize(corpus, RagEvaluationJson.Options));
    private static bool ValidReview(RagFeedbackReview review, string digest) => review is not null && Key().IsMatch(review.ReviewerKey)
        && Key().IsMatch(review.EvidenceKey) && review.ContentSha256 == digest && review.DecisionKey == "approved"
        && review.SourceUseAllowed && review.RedactionConfirmed && review.InstructionRiskReviewed;

    private static void ValidateSample(RagFeedbackSample sample)
    {
        Require(sample is not null && Key().IsMatch(sample.SampleKey) && Key().IsMatch(sample.AuthorKey)
            && HashKey().IsMatch(sample.SourceSha256) && sample.Case is not null && sample.Chunks is { Length: > 0 and <= 16 }, "invalid_feedback_sample");
        var testCase = sample.Case; var source = "reviewed-" + sample.SampleKey + "-v1";
        Require(testCase.CaseId == "reviewed-" + sample.SampleKey && testCase.TenantScope == "reviewed"
            && new[] { "terminology", "identifier", "table", "cross_paragraph", "version_conflict", "no_answer", "cross_tenant", "revoked", "prompt_injection", "deleted", "history_dependency" }.Contains(testCase.Category)
            && SafeText(testCase.Question, 2048) && testCase.AllowedSourceVersions is [var allowed] && allowed == source
            && testCase.ExpectedChunkIds is { Length: <= 16 } && testCase.ForbiddenOutputMarkers is { Length: <= 16 }, "invalid_feedback_sample");
        Require(!new[] { "no_answer", "cross_tenant", "revoked", "prompt_injection", "deleted", "history_dependency" }.Contains(testCase.Category)
            || testCase.IsSafetyCase && testCase.ShouldRefuse, "feedback_sample_safety_required");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in sample.Chunks)
            Require(chunk is not null && chunk.ChunkId.StartsWith("reviewed-" + sample.SampleKey + "-", StringComparison.Ordinal)
                && Key().IsMatch(chunk.ChunkId) && ids.Add(chunk.ChunkId) && chunk.TenantScope == "reviewed"
                && chunk.SourceVersion == source && chunk.Location == "redacted" && SafeText(chunk.Text, 4096), "invalid_feedback_sample_chunk");
        Require(testCase.ExpectedChunkIds.Distinct(StringComparer.Ordinal).Count() == testCase.ExpectedChunkIds.Length
            && testCase.ExpectedChunkIds.All(x => x is not null && ids.Contains(x))
            && (testCase.ShouldRefuse ? testCase.ExpectedChunkIds.Length == 0 : testCase.ExpectedChunkIds.Length > 0)
            && testCase.ForbiddenOutputMarkers.All(x => SafeText(x, 128)), "invalid_feedback_sample_evidence");
    }

    // 仅拒绝明显的地址、账号与标识格式；不能检测任意姓名、改写、编码或隐私内容，也不自动改写正文。
    private static bool SafeText(string text, int maximum) => !string.IsNullOrWhiteSpace(text) && text.Length <= maximum
        && !text.Any(x => char.IsControl(x) && x is not '\n' and not '\r' and not '\t') && !SensitiveText().IsMatch(text);
    private static void Require([DoesNotReturnIf(false)] bool valid, string code) { if (!valid) throw new InvalidDataException(code); }
    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)] private static partial Regex Key();
    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)] private static partial Regex HashKey();
    [GeneratedRegex("^fullnet-rag-reviewed-[a-z0-9-]{1,64}-v[1-9][0-9]{0,3}$", RegexOptions.CultureInvariant)] private static partial Regex DatasetKey();
    [GeneratedRegex(@"@|https?://|\b(?:\d{1,3}\.){3}\d{1,3}\b|\d{7,}|[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}|(?:password|api[_-]?key|secret|token)\s*[:=]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveText();
}
