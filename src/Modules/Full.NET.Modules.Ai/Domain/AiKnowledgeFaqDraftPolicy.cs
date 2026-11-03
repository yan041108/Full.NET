using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>草稿有界且确定性规范化；同义问法重复必须显式处理，不能静默去重或截断。</summary>
internal static class AiKnowledgeFaqDraftPolicy
{
    // 单字段上限合计不足一万七千个 UTF-16 单元；JSON 最坏转义需要六倍空间，读写必须共用预算。
    private const int MaximumContentJsonUtf8Bytes = 128 * 1024;
    internal static AiKnowledgeFaqContent Freeze(AiKnowledgeFaqContent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var question = Text(value.Question, 512, false); var answer = Text(value.Answer, 8000, true); var category = Text(value.Category, 64, false);
        if (value.Aliases is null || value.Aliases.Count > 16) throw Invalid();
        var aliases = value.Aliases.Select(alias => Text(alias, 512, false)).ToArray();
        if (aliases.Append(question).Select(QuestionKey).Distinct(StringComparer.Ordinal).Count() != aliases.Length + 1) throw Invalid();
        var from = Time(value.ValidFromUtc); var until = Time(value.ExpiresAtUtc);
        if (from.HasValue && until.HasValue && until <= from) throw Invalid();
        return new(question, Array.AsReadOnly(aliases), answer, category, from, until);
    }
    internal static string Basis(string value) => Text(value, 1024, true);
    internal static string Encode(AiKnowledgeFaqContent value)
    {
        var json = JsonSerializer.Serialize(Freeze(value), AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqContent);
        if (Encoding.UTF8.GetByteCount(json) > MaximumContentJsonUtf8Bytes) throw Invalid();
        return json;
    }
    internal static AiKnowledgeFaqContent Decode(string json)
    {
        if (json is null || json.Length > MaximumContentJsonUtf8Bytes || Encoding.UTF8.GetByteCount(json) > MaximumContentJsonUtf8Bytes) throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            Fields(document.RootElement, "question", "aliases", "answer", "category", "validFromUtc", "expiresAtUtc");
            return Freeze(JsonSerializer.Deserialize(json, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqContent) ?? throw Invalid());
        }
        catch (JsonException) { throw Invalid(); }
    }
    internal static string QuestionKey(string question) => string.Join(' ', question.Normalize(NormalizationForm.FormKC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(text)));
    internal static string ContentHash(string json, string sourceHash) => Hash("faq_draft_v1\n" + sourceHash + "\n" + json);
    internal static void Key(Guid value) { if (value == Guid.Empty || value.Version != 7) throw Invalid(); }
    internal static void Fields(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != fields.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Any(name => !fields.Contains(name, StringComparer.Ordinal))) throw Invalid();
    }
    private static string Text(string value, int limit, bool lines)
    {
        if (value is null) throw Invalid(); var text = value.Trim();
        if (text.Length is < 1 || text.Length > limit || text.Any(character => char.IsControl(character) && (!lines || character is not ('\r' or '\n' or '\t')))) throw Invalid();
        try { _ = new UTF8Encoding(false, true).GetByteCount(text); } catch (EncoderFallbackException) { throw Invalid(); }
        return text;
    }
    private static DateTimeOffset? Time(DateTimeOffset? value)
    {
        if (!value.HasValue) return null;
        if (value.Value.UtcDateTime.Year < 1000) throw Invalid();
        return new DateTimeOffset(value.Value.UtcTicks - value.Value.UtcTicks % 10, TimeSpan.Zero);
    }
    private static InvalidDataException Invalid() => new("FAQ 草稿内容、请求身份或有效期无效。");
}
