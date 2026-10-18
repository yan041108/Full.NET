using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>只限制候选结构和规模；内容脱敏由明确的人工作业确认，不推断匿名性。</summary>
internal static class AiFeedbackEvaluationPolicy
{
    internal static bool Valid(AiFeedbackEvaluationSample? sample) => sample is not null
        && sample.LanguageTag is "zh-CN" or "en-US"
        && Text(sample.Question, 2048) && Text(sample.ExpectedAnswer, 4096) && Text(sample.Evidence, 8192);

    internal static string Hash(AiFeedbackEvaluationSample sample) => Digest(JsonSerializer.Serialize(sample, AiJsonSerializerContext.Default.AiFeedbackEvaluationSample));
    internal static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Text(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum
        && !string.IsNullOrWhiteSpace(value) && AiPromptPolicy.ValidUnicode(value)
        && !value.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t'));
}
