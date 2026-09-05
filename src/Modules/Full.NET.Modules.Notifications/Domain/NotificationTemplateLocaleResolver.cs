using Full.NET.Abstractions.Results;
using Full.NET.Localization;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Domain;

/// <summary>
/// 将 BCP 47 语言标签规范化为仓库支持集，并按默认语言与已发布变体做闭合回退。
/// </summary>
internal static class NotificationTemplateLocaleResolver
{
    /// <summary>把输入别名或标签规范化为受支持 BCP 47 标签。</summary>
    /// <param name="localeTag">原始语言标签或别名。</param>
    public static Result<string> NormalizeLocaleTag(string? localeTag)
    {
        var normalized = localeTag?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 35)
        {
            return Result<string>.Failure(TemplateValidation("The locale tag is invalid."));
        }

        if (LocaleCatalog.Aliases.TryGetValue(normalized, out var alias))
        {
            normalized = alias;
        }

        return LocaleCatalog.SupportedLocales.Contains(normalized, StringComparer.Ordinal)
            ? Result<string>.Success(normalized)
            : Result<string>.Failure(TemplateValidation("The locale tag is not supported."));
    }

    /// <summary>在已发布语言集合中按偏好、别名链与默认语言挑选最终标签。</summary>
    /// <param name="publishedLocaleTags">当前模板键下已发布变体的语言标签。</param>
    /// <param name="preferredLocaleTag">收件人偏好语言。</param>
    /// <param name="defaultLocaleTag">模板声明的默认语言。</param>
    public static string? PickPublishedLocale(
        IReadOnlyCollection<string> publishedLocaleTags,
        string preferredLocaleTag,
        string defaultLocaleTag)
    {
        if (publishedLocaleTags.Count == 0)
        {
            return null;
        }

        var published = publishedLocaleTags.ToHashSet(StringComparer.Ordinal);
        if (published.Contains(preferredLocaleTag))
        {
            return preferredLocaleTag;
        }

        foreach (var candidate in BuildFallbackChain(preferredLocaleTag))
        {
            if (published.Contains(candidate))
            {
                return candidate;
            }
        }

        return published.Contains(defaultLocaleTag)
            ? defaultLocaleTag
            : published.OrderBy(tag => tag, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>计算受支持语言中尚未发布的标签，供管理端缺失提示。</summary>
    /// <param name="publishedLocaleTags">已发布语言标签。</param>
    public static IReadOnlyList<string> MissingSupportedLocales(
        IReadOnlyCollection<string> publishedLocaleTags)
    {
        var published = publishedLocaleTags.ToHashSet(StringComparer.Ordinal);
        return LocaleCatalog.SupportedLocales
            .Where(locale => !published.Contains(locale))
            .ToArray();
    }

    private static IEnumerable<string> BuildFallbackChain(string localeTag)
    {
        yield return localeTag;
        if (LocaleCatalog.Aliases.TryGetValue(localeTag, out var alias))
        {
            yield return alias;
        }

        var dash = localeTag.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0)
        {
            yield return localeTag[..dash];
        }
    }

    private static Error TemplateValidation(string message) =>
        new(NotificationsErrorCodes.TemplateValidationFailed, message, ErrorType.Validation);
}
