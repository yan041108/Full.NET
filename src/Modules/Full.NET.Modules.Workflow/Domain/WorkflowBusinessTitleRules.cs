using System.Text;
using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>业务标题模板校验与占位符替换规则；禁止执行模板代码或拼接任意 URL。</summary>
internal static class WorkflowBusinessTitleRules
{
    /// <summary>模板允许的最大长度。</summary>
    public const int MaxTemplateLength = 256;

    /// <summary>标题快照允许的最大长度。</summary>
    public const int MaxTitleLength = 256;

    /// <summary>校验模板长度是否在允许范围内。</summary>
    /// <param name="template">业务标题模板。</param>
    /// <returns>为空或长度合法时返回 <see langword="true"/>。</returns>
    public static bool IsValidTemplate(string? template) =>
        template is null || template.Length <= MaxTemplateLength;

    /// <summary>校验显式标题覆盖值是否在允许范围内。</summary>
    /// <param name="title">调用方提供的标题快照。</param>
    /// <returns>为空或长度合法时返回 <see langword="true"/>。</returns>
    public static bool IsValidTitle(string? title) =>
        title is null || (title.Length >= 1 && title.Length <= MaxTitleLength);

    /// <summary>规范化模板；空白输入视为未配置。</summary>
    /// <param name="template">原始模板文本。</param>
    /// <returns>去首尾空白后的模板，或 <see langword="null"/>。</returns>
    public static string? NormalizeTemplate(string? template) =>
        string.IsNullOrWhiteSpace(template) ? null : template.Trim();

    /// <summary>规范化显式标题；空白输入视为未提供。</summary>
    /// <param name="title">原始标题文本。</param>
    /// <returns>去首尾空白后的标题，或 <see langword="null"/>。</returns>
    public static string? NormalizeTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? null : title.Trim();

    /// <summary>
    /// 从已发布模板与启动表单初始值解析业务标题快照；
    /// 任一占位符无法解析或结果为空时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="template">版本化业务标题模板。</param>
    /// <param name="initialValues">启动时提交的表单初始值。</param>
    /// <returns>解析后的标题快照，或 <see langword="null"/>。</returns>
    public static string? Resolve(string? template, JsonElement initialValues)
    {
        var normalizedTemplate = NormalizeTemplate(template);
        if (normalizedTemplate is null)
        {
            return null;
        }

        var builder = new StringBuilder(normalizedTemplate.Length);
        for (var index = 0; index < normalizedTemplate.Length; index++)
        {
            if (normalizedTemplate[index] != '{')
            {
                builder.Append(normalizedTemplate[index]);
                continue;
            }

            var end = normalizedTemplate.IndexOf('}', index + 1);
            if (end < 0)
            {
                return null;
            }

            var path = normalizedTemplate[(index + 1)..end].Trim();
            if (path.Length == 0 || !TryReadPath(initialValues, path, out var value))
            {
                return null;
            }

            builder.Append(value);
            index = end;
        }

        var resolved = builder.ToString().Trim();
        return resolved.Length == 0 || resolved.Length > MaxTitleLength ? null : resolved;
    }

    /// <summary>按点分路径读取 JSON 标量并格式化为展示文本。</summary>
    /// <param name="root">根 JSON 对象。</param>
    /// <param name="path">字段路径，例如 <c>summary</c> 或 <c>request.summary</c>。</param>
    /// <param name="value">解析后的展示文本。</param>
    /// <returns>路径存在且值为非空白标量时返回 <see langword="true"/>。</returns>
    private static bool TryReadPath(JsonElement root, string path, out string value)
    {
        value = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var current = root;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segments[index], out var next))
            {
                return false;
            }

            if (index == segments.Length - 1)
            {
                return TryFormatScalar(next, out value);
            }

            current = next;
        }

        return false;
    }

    /// <summary>把 JSON 标量格式化为单行展示文本。</summary>
    /// <param name="element">待格式化的 JSON 元素。</param>
    /// <param name="value">格式化结果。</param>
    /// <returns>值为非空白标量时返回 <see langword="true"/>。</returns>
    private static bool TryFormatScalar(JsonElement element, out string value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = element.GetString() ?? string.Empty;
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                value = element.ToString();
                break;
            default:
                value = string.Empty;
                return false;
        }

        value = value.Trim();
        return value.Length > 0;
    }
}
