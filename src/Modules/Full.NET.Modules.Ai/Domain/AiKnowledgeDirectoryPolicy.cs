using System.Text;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>人工标签仅规范展示文本；文件夹深度和批量上限均有界，不参与授权继承。</summary>
internal static class AiKnowledgeDirectoryPolicy
{
    internal const int MaxFolders = 500;
    internal static string? Name(string? value)
    {
        try
        {
            var name = value?.Trim().Normalize(NormalizationForm.FormKC);
            return name is { Length: > 0 and <= 100 } && !name.Any(char.IsControl) ? name : null;
        }
        catch (ArgumentException) { return null; }
    }
    internal static string[]? Tags(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count > 10) return null;
        try
        {
            var tags = values.Select(value => value?.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant()).ToArray();
            if (tags.Any(tag => tag is not { Length: > 0 and <= 64 } || tag.Any(char.IsControl))) return null;
            return tags.Select(tag => tag!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }
        catch (ArgumentException) { return null; }
    }
}
