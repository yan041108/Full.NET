using Full.NET.Modules.Ai.Contracts;
using Full.NET.AI.Abstractions.Budgets;
using System.Text;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>固定纯文本策略的准入与单次替换；不解释用户输入或动态 Schema。</summary>
internal static class AiPromptPolicy
{
    private const string Placeholder = "{{input}}";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool IsValid(AiPromptBody? body)
    {
        if (body is null || body.ModelConfigId == Guid.Empty || body.LanguageTag is not ("en-US" or "zh-CN")
            || body.Template is not { Length: > 0 and <= 8192 } template
            || body.InputTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens
            || body.OutputTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens) return false;
        var index = template.IndexOf(Placeholder, StringComparison.Ordinal);
        if (index < 0 || index != template.LastIndexOf(Placeholder, StringComparison.Ordinal)) return false;
        // 模板只有一个受控槽位；其他表达式不被接受，输入内容始终作为字面数据。
        var remainder = template.Remove(index, Placeholder.Length);
        return !remainder.Contains("{{", StringComparison.Ordinal) && !remainder.Contains("}}", StringComparison.Ordinal)
            && ValidUnicode(template);
    }

    internal static string? Compose(AiPromptBody body, string input)
    {
        if (!IsValid(body) || string.IsNullOrWhiteSpace(input) || input.Length > 16_384
            || body.Template.Length - Placeholder.Length + input.Length > 16_384 || !ValidUnicode(input)) return null;
        // Replace 只扫描原模板，输入里出现同名槽位也不会被二次解释。
        return body.Template.Replace(Placeholder, input, StringComparison.Ordinal);
    }

    internal static bool ValidUnicode(string text)
    {
        try { _ = StrictUtf8.GetByteCount(text); return true; }
        catch (EncoderFallbackException) { return false; }
    }
}
