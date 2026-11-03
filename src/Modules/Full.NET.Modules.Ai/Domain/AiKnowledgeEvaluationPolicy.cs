using System.Text;
using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>来源足迹单调增加；移出样例、恢复或复制均不能把已派生正文降级为独立内容。</summary>
internal sealed record AiKnowledgeEvaluationFootprint(int Version, IReadOnlyList<string> Sources, IReadOnlyList<Guid> Contributors);
internal sealed record AiKnowledgeEvaluationBody(int Version, string Name, string State, IReadOnlyList<AiKnowledgeEvaluationCase> Cases);

/// <summary>同一规范 JSON 在写前和读后共用预算；拒绝截断、重复身份及来源漂白。</summary>
internal static class AiKnowledgeEvaluationPolicy
{
    internal const int MaximumCases = 8;
    internal const int MaximumSources = 8;
    internal const int MaximumContributors = 16;
    internal const int MaximumBodyBytes = 768 * 1024;
    internal const int MaximumFootprintBytes = 320 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static string Encode(AiKnowledgeEvaluationBody body)
    {
        Validate(body);
        return Budget(JsonSerializer.Serialize(body, AiKnowledgeEvaluationJsonContext.Default.AiKnowledgeEvaluationBody), MaximumBodyBytes);
    }

    internal static AiKnowledgeEvaluationBody Decode(string json)
    {
        try
        {
            Budget(json, MaximumBodyBytes);
            var value = JsonSerializer.Deserialize(json, AiKnowledgeEvaluationJsonContext.Default.AiKnowledgeEvaluationBody) ?? throw Invalid();
            if (Encode(value) != json) throw Invalid();
            return value;
        }
        catch (Exception error) when (error is JsonException or EncoderFallbackException or ArgumentException) { throw Invalid(); }
    }

    internal static string Encode(AiKnowledgeEvaluationFootprint footprint)
    {
        if (footprint.Version != 1 || footprint.Sources is null || footprint.Contributors is null
            || footprint.Sources.Count > MaximumSources || footprint.Contributors.Count is < 1 or > MaximumContributors
            || footprint.Contributors.Any(id => id == Guid.Empty) || footprint.Contributors.Distinct().Count() != footprint.Contributors.Count
            || footprint.Sources.Distinct(StringComparer.Ordinal).Count() != footprint.Sources.Count) throw Invalid();
        var feedback = new HashSet<Guid>();
        foreach (var source in footprint.Sources)
        {
            var origin = Origin(source);
            if (AiKnowledgeFaqProvenanceCodec.Encode(origin) != source) throw Invalid();
            if (origin.Feedback is null || !feedback.Add(origin.Feedback.FeedbackId)) throw Invalid();
        }
        return Budget(JsonSerializer.Serialize(footprint, AiKnowledgeEvaluationJsonContext.Default.AiKnowledgeEvaluationFootprint), MaximumFootprintBytes);
    }

    internal static AiKnowledgeEvaluationFootprint DecodeFootprint(string json)
    {
        try
        {
            Budget(json, MaximumFootprintBytes);
            var value = JsonSerializer.Deserialize(json, AiKnowledgeEvaluationJsonContext.Default.AiKnowledgeEvaluationFootprint) ?? throw Invalid();
            if (Encode(value) != json) throw Invalid();
            return value;
        }
        catch (Exception error) when (error is JsonException or EncoderFallbackException or ArgumentException) { throw Invalid(); }
    }

    internal static AiKnowledgeEvaluationFootprint Extend(AiKnowledgeEvaluationFootprint? previous, IEnumerable<string> added, Guid actor)
    {
        if (actor == Guid.Empty) throw Invalid();
        var sources = (previous?.Sources ?? []).Concat(added).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var result = new AiKnowledgeEvaluationFootprint(1, sources,
            (previous?.Contributors ?? []).Append(actor).Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray());
        _ = Encode(result);
        return result;
    }

    internal static void Match(AiKnowledgeEvaluationBody body, AiKnowledgeEvaluationFootprint footprint)
    {
        var keys = footprint.Sources.Select(Hash).ToHashSet(StringComparer.Ordinal);
        if (body.Cases.Any(item => item.SourceKey is { } key && !keys.Contains(key))) throw Invalid();
    }

    internal static AiKnowledgeFaqProvenance Origin(string json)
    {
        Budget(json, 16384);
        var projected = JsonSerializer.Deserialize(json, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqProvenance) ?? throw Invalid();
        return AiKnowledgeFaqProvenanceCodec.Decode(new AiKnowledgeFaqDraftRecord
        {
            OriginKey = "feedback_derived", SourceJson = json, SourceHash = AiKnowledgeFaqDraftPolicy.Hash(json),
            TenantId = projected.Feedback?.TenantId
        });
    }

    internal static bool CanApprove(AiKnowledgeEvaluationBody body, AiKnowledgeEvaluationFootprint footprint, Guid actor) =>
        body.State == "frozen" && actor != Guid.Empty && !footprint.Contributors.Contains(actor);
    internal static string Hash(string value) => AiFeedbackEvaluationPolicy.Digest(value);
    internal static string ContentHash(string body, string source) => Hash("knowledge-evaluation.v1\n" + source + "\n" + body);
    internal static bool Text(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum
        && !string.IsNullOrWhiteSpace(value) && AiPromptPolicy.ValidUnicode(value)
        && !value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));

    private static void Validate(AiKnowledgeEvaluationBody body)
    {
        if (body.Version != 1 || body.Cases is null || !Text(body.Name, 160) || body.State is not ("draft" or "frozen")
            || body.Cases.Count is < 1 or > MaximumCases || body.Cases.Any(item => item is null)
            || body.Cases.Select(item => item.Id).Distinct().Count() != body.Cases.Count) throw Invalid();
        foreach (var item in body.Cases)
            if (item is null || item.Id == Guid.Empty || !AiFeedbackEvaluationPolicy.Valid(item.Sample)
                || (item.SourceKey is null ? !Text(item.IndependentBasis, 1024) : item.IndependentBasis is not null || item.SourceKey.Length != 64 || !item.SourceKey.All(Uri.IsHexDigit))) throw Invalid();
    }

    private static string Budget(string value, int maximum)
    {
        if (value is null || value.Length > maximum || Utf8.GetByteCount(value) > maximum) throw Invalid();
        return value;
    }
    private static InvalidDataException Invalid() => new("评估集规范内容、身份或来源预算无效。");
}
